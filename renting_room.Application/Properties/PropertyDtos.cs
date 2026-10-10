using renting_room.Application.Common.Interfaces;
using renting_room.Domain.Common;
using renting_room.Domain.Identity;
using renting_room.Domain.Properties;

namespace renting_room.Application.Properties;

public sealed record AddressInput(string StreetAddress, string CommuneName, string ProvinceName, string? CommuneCode, string? ProvinceCode);

public sealed record LandParcelInput(string? ParcelNo, string? MapSheetNo, string? OwnershipCertificateNo);

/// <summary>
/// Cài đặt kỳ thu của khu (PR-BR-09): ngày chốt 1–28, thu trước / thu sau, hạn thanh toán, tính kỳ lẻ, số ngày báo trước gợi ý,
/// làm tròn tổng phiếu xuống nghìn (BL-BR-29 — null = bật).
/// </summary>
public sealed record PropertyBillingInput(
    int AnchorDay, ChargeMode ChargeMode, int PaymentDueDays, ProrationMode ProrationMode, int NoticeDays, bool? RoundInvoiceTotal = null);

/// <summary>Một lần đổi ngày chốt / cách thu khi khu đã có phiếu (K4): kỳ chuyển tiếp [EffectiveFrom, TransitionEnd].</summary>
public sealed record BillingChangeDto(
    DateOnly EffectiveFrom, DateOnly TransitionEnd, int AnchorDay, ChargeMode ChargeMode, int DeviationDays, int AdjustDays);

public sealed record PropertyBillingDto(
    int AnchorDay, ChargeMode ChargeMode, int PaymentDueDays, ProrationMode ProrationMode, int NoticeDays,
    IReadOnlyList<BillingChangeDto> Changes, bool RoundInvoiceTotal = true);

public sealed record PropertySummaryDto(
    Guid Id,
    string Code,
    string Name,
    string AddressText,
    int RoomCount,
    int OccupiedRoomCount,
    bool LessorComplete,
    bool IsArchived);

/// <summary>Số giấy tờ bên cho thuê chỉ trả 4 số cuối (LEG-06) — xem đầy đủ qua endpoint reveal có ghi log.</summary>
public sealed record LessorDto(
    LessorType Type,
    string Name,
    string Address,
    string Phone,
    string? Email,
    IdDocumentType? IdType,
    string? IdNumberMasked,
    DateOnly? IdIssueDate,
    string? IdIssuePlace,
    DateOnly? DateOfBirth,
    string? TaxCode,
    string? RepresentativeName,
    string? RepresentativeTitle,
    string? AuthorizationDocNo,
    DateOnly? AuthorizationDocDate,
    bool IsComplete);

public sealed record PropertyDetailDto(
    Guid Id,
    string Code,
    string Name,
    AddressInput Address,
    string AddressText,
    string? Description,
    string? EvnCustomerCode,
    LandParcelInput Land,
    PropertyBillingDto Billing,
    LessorDto? Lessor,
    BankAccount? BankAccount,
    string? HouseRulesText,
    bool IsArchived,
    DateTimeOffset CreatedAt,
    string Version,
    bool LessorInherited = false);

internal static class PropertyMapping
{
    /// <summary><c>lessor</c> = bên cho thuê hiệu lực (PR-BR-17): riêng của khu, không có thì của chủ trọ (<c>LessorInherited</c>).</summary>
    public static async Task<PropertyDetailDto> ToDetailAsync(this Property p, IAppDbContext db, DateOnly today, CancellationToken ct)
    {
        var (lessor, inherited) = await LessorSource.EffectiveAsync(db, p, ct);
        return p.ToDetail(today, lessor, inherited);
    }

    private static PropertyDetailDto ToDetail(this Property p, DateOnly today, LessorDetails? lessor, bool inherited) => new(
        p.Id,
        p.Code,
        p.Name,
        new AddressInput(p.StreetAddress, p.CommuneName, p.ProvinceName, p.CommuneCode, p.ProvinceCode),
        p.Address.FullText,
        p.Description,
        p.EvnCustomerCode,
        new LandParcelInput(p.LandParcelNo, p.LandMapSheetNo, p.OwnershipCertificateNo),
        p.ToBillingDto(),
        lessor?.ToDto(today),
        p.BankAccount,
        p.HouseRulesText,
        p.IsArchived,
        p.CreatedAt,
        p.Version.ToString(),
        lessor is not null && inherited);

    public static LessorDto ToDto(this LessorDetails l, DateOnly today) => new(
        l.Type, l.Name, l.Address, l.Phone, l.Email, l.IdType,
        l.IdNumberLast4 is null ? null : $"********{l.IdNumberLast4}",
        l.IdIssueDate, l.IdIssuePlace, l.DateOfBirth, l.TaxCode, l.RepresentativeName, l.RepresentativeTitle,
        l.AuthorizationDocNo, l.AuthorizationDocDate, l.IsComplete(today));

    public static OrganizationLessorDto ToLessorDto(this Organization o, DateOnly today) =>
        new(o.DefaultLessor?.ToDto(today), new LessorPrefillDto(o.ContactName ?? o.Name, o.ContactPhone, o.Address));

    public static PropertyAddress ToDomain(this AddressInput a) =>
        new(a.StreetAddress, a.CommuneName, a.ProvinceName, a.CommuneCode, a.ProvinceCode);

    public static LandParcel ToDomain(this LandParcelInput? land) =>
        new(land?.ParcelNo, land?.MapSheetNo, land?.OwnershipCertificateNo);

    public static BillingSettings ToDomain(this PropertyBillingInput b) =>
        new(b.AnchorDay, b.ChargeMode, b.PaymentDueDays, b.ProrationMode, b.NoticeDays, b.RoundInvoiceTotal ?? true);

    public static PropertyBillingDto ToBillingDto(this Property p)
    {
        var schedule = p.BillingSchedule;
        var changes = schedule.Entries.Skip(1).Select(e =>
        {
            var transition = schedule.StandardPeriodContaining(e.EffectiveFrom);
            return new BillingChangeDto(e.EffectiveFrom, transition.End, e.AnchorDay, e.ChargeMode, transition.DeviationDays, e.AdjustDays);
        }).ToList();
        return new PropertyBillingDto(p.BillingAnchorDay, p.ChargeMode, p.PaymentDueDays, p.ProrationMode, p.DefaultNoticeDays, changes,
            p.RoundInvoiceTotal);
    }
}
