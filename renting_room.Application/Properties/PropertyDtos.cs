using renting_room.Domain.Common;
using renting_room.Domain.Properties;

namespace renting_room.Application.Properties;

public sealed record AddressInput(string StreetAddress, string CommuneName, string ProvinceName, string? CommuneCode, string? ProvinceCode);

public sealed record LandParcelInput(string? ParcelNo, string? MapSheetNo, string? OwnershipCertificateNo);

public sealed record BillingDefaultsInput(int AnchorDay, ChargeMode ChargeMode, int PaymentDueDays, ProrationMode ProrationMode, int NoticeDays);

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
    BillingDefaultsInput BillingDefaults,
    LessorDto? Lessor,
    BankAccount? BankAccount,
    string? HouseRulesText,
    bool IsArchived,
    DateTimeOffset CreatedAt,
    string Version);

internal static class PropertyMapping
{
    public static PropertyDetailDto ToDetail(this Property p, DateOnly today) => new(
        p.Id,
        p.Code,
        p.Name,
        new AddressInput(p.StreetAddress, p.CommuneName, p.ProvinceName, p.CommuneCode, p.ProvinceCode),
        p.Address.FullText,
        p.Description,
        p.EvnCustomerCode,
        new LandParcelInput(p.LandParcelNo, p.LandMapSheetNo, p.OwnershipCertificateNo),
        new BillingDefaultsInput(p.DefaultBillingAnchorDay, p.DefaultChargeMode, p.DefaultPaymentDueDays, p.DefaultProrationMode, p.DefaultNoticeDays),
        p.Lessor is { } l
            ? new LessorDto(
                l.Type, l.Name, l.Address, l.Phone, l.Email, l.IdType,
                l.IdNumberLast4 is null ? null : $"********{l.IdNumberLast4}",
                l.IdIssueDate, l.IdIssuePlace, l.DateOfBirth, l.TaxCode, l.RepresentativeName, l.RepresentativeTitle,
                l.AuthorizationDocNo, l.AuthorizationDocDate, l.IsComplete(today))
            : null,
        p.BankAccount,
        p.HouseRulesText,
        p.IsArchived,
        p.CreatedAt,
        p.Version.ToString());

    public static PropertyAddress ToDomain(this AddressInput a) =>
        new(a.StreetAddress, a.CommuneName, a.ProvinceName, a.CommuneCode, a.ProvinceCode);

    public static LandParcel ToDomain(this LandParcelInput? land) =>
        new(land?.ParcelNo, land?.MapSheetNo, land?.OwnershipCertificateNo);

    public static BillingDefaults ToDomain(this BillingDefaultsInput b) =>
        new(b.AnchorDay, b.ChargeMode, b.PaymentDueDays, b.ProrationMode, b.NoticeDays);
}
