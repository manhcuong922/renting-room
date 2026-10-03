using FluentValidation;
using Mediator;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using renting_room.Application.Common.Interfaces;
using renting_room.Application.Common.Validation;
using renting_room.Domain.Common;
using renting_room.Domain.Contracts;
using renting_room.Domain.Properties;
using renting_room.Domain.Renters;

namespace renting_room.Application.Exports;

public enum RenterExportLayout
{
    SheetPerProperty, // mỗi khu 1 sheet (mặc định)
    SheetPerFloor,    // mỗi tầng của mỗi khu 1 sheet
    SingleSheet       // gộp 1 sheet
}

/// <summary>
/// E1 — Danh sách người thuê (M10). Bộ lọc kết hợp AND; danh sách rỗng / null = không lọc.
/// Khoảng ngày: người <b>đang ở</b> bất kỳ ngày nào trong [from, to]; bỏ trống = đang ở hôm nay.
/// </summary>
public sealed record ExportRentersQuery(
    IReadOnlyList<Guid>? PropertyIds = null,
    IReadOnlyList<string>? Floors = null,
    IReadOnlyList<Guid>? RoomGroupIds = null,
    IReadOnlyList<Guid>? RoomIds = null,
    DateOnly? FromDate = null,
    DateOnly? ToDate = null,
    RenterExportLayout Layout = RenterExportLayout.SheetPerProperty,
    bool IncludeSensitive = false) : IRequest<Result<ExportFile>>;

public sealed record ExportFile(string FileName, byte[] Content);

public sealed class ExportRentersQueryValidator : AbstractValidator<ExportRentersQuery>
{
    public const int MaxRangeDays = 3660;

    public ExportRentersQueryValidator()
    {
        RuleFor(x => x.PropertyIds).Must(ids => ids is null || ids.Count <= 100).WithErrorCode("OUT_OF_RANGE").WithMessage("Tối đa 100 khu.");
        RuleFor(x => x.RoomGroupIds).Must(ids => ids is null || ids.Count <= 100).WithErrorCode("OUT_OF_RANGE").WithMessage("Tối đa 100 nhóm phòng.");
        RuleFor(x => x.RoomIds).Must(ids => ids is null || ids.Count <= 500).WithErrorCode("OUT_OF_RANGE").WithMessage("Tối đa 500 phòng.");
        RuleFor(x => x.Floors).Must(f => f is null || f.Count <= 50).WithErrorCode("OUT_OF_RANGE").WithMessage("Tối đa 50 tầng.");
        RuleForEach(x => x.Floors).OptionalText(10);
        RuleFor(x => x.Layout).IsInEnum();
        RuleFor(x => x.ToDate)
            .Must((x, to) => x.FromDate is null || to is null || (to >= x.FromDate && to.Value.DayNumber - x.FromDate.Value.DayNumber <= MaxRangeDays))
            .WithErrorCode("INVALID_DATE_RANGE").WithMessage("Đến ngày phải ≥ từ ngày và khoảng tối đa 10 năm.");
    }
}

public static class ExportErrors
{
    public const int MaxRows = 20_000;

    public static readonly Error TooLarge = Error.BusinessRule("EXPORT_TOO_LARGE",
        $"Kết quả vượt {MaxRows:N0} dòng — hãy thu hẹp bộ lọc (ít khu hơn / khoảng ngày ngắn hơn).");
}

/// <summary>
/// RP-BR-01: id khu / nhóm / phòng không thuộc tổ chức ⇒ 404 cả request. RP-BR-03: số giấy tờ che mặc định;
/// xuất đầy đủ ghi log AUDIT. RP-BR-05: tối đa 20.000 dòng.
/// </summary>
public sealed class ExportRentersHandler(
    IAppDbContext db,
    ICurrentUser currentUser,
    IPersonalDataProtector protector,
    ISpreadsheetWriter writer,
    TimeProvider clock,
    ILogger<ExportRentersHandler> logger)
    : IRequestHandler<ExportRentersQuery, Result<ExportFile>>
{
    public async ValueTask<Result<ExportFile>> Handle(ExportRentersQuery request, CancellationToken cancellationToken)
    {
        var missing = await FindMissingReferenceAsync(request, cancellationToken);
        if (missing is not null)
            return missing;

        var now = clock.GetUtcNow();
        var today = now.ToBusinessDate();
        var from = request.FromDate ?? request.ToDate ?? today;
        var to = request.ToDate ?? request.FromDate ?? today;

        var query = RenterExportQuery.Build(db, request, from, to);
        if (await query.CountAsync(cancellationToken) > ExportErrors.MaxRows)
            return ExportErrors.TooLarge;

        // Trong mỗi hợp đồng: chủ hộ trước, rồi người đứng tên, sau đó theo thứ tự quan hệ như sổ hộ (vợ/chồng → cha mẹ → con → …).
        var rows = (await query.ToListAsync(cancellationToken))
            .OrderBy(r => r.PropertyCode, StringComparer.Ordinal).ThenBy(r => r.Floor, StringComparer.Ordinal)
            .ThenBy(r => r.RoomCode, StringComparer.Ordinal).ThenBy(r => r.ContractNo, StringComparer.Ordinal)
            .ThenByDescending(r => r.IsHouseholdHead).ThenByDescending(r => r.IsRepresentative)
            .ThenBy(r => r.RelationshipType ?? (OccupantRelationship)int.MaxValue)
            .ThenBy(r => r.FullName, StringComparer.Ordinal)
            .ToList();

        if (request.IncludeSensitive)
            logger.LogWarning("AUDIT: user {UserId} exported {RowCount} renters with full id numbers (properties {PropertyIds})",
                currentUser.UserId, rows.Count, request.PropertyIds is { Count: > 0 } ids ? string.Join(",", ids) : "all");

        var subtitle = RenterExportSheets.Subtitle(from, to, today, now, request.IncludeSensitive);
        var idNumber = (RenterExportRow r) => request.IncludeSensitive
            ? protector.Decrypt(r.IdNumberEncrypted)
            : PersonalDataProtectorExtensions.Mask(r.IdNumberLast4);
        var document = new SpreadsheetDocument(RenterExportSheets.Build(rows, request.Layout, subtitle, idNumber));

        var fileName = $"danh-sach-nguoi-thue_{now.ToOffset(VietnamTime.Offset):yyyyMMdd-HHmm}.xlsx";
        return new ExportFile(fileName, writer.Write(document));
    }

    private async Task<Error?> FindMissingReferenceAsync(ExportRentersQuery request, CancellationToken ct)
    {
        if (request.PropertyIds is { Count: > 0 } propertyIds)
        {
            var ids = propertyIds.Distinct().ToList();
            if (await db.Properties.CountAsync(p => ids.Contains(p.Id), ct) != ids.Count)
                return PropertyErrors.PropertyNotFound;
        }
        if (request.RoomGroupIds is { Count: > 0 } groupIds)
        {
            var ids = groupIds.Distinct().ToList();
            if (await db.RoomGroups.CountAsync(g => ids.Contains(g.Id), ct) != ids.Count)
                return PropertyErrors.RoomGroupNotFound;
        }
        if (request.RoomIds is { Count: > 0 } roomIds)
        {
            var ids = roomIds.Distinct().ToList();
            if (await db.Rooms.CountAsync(r => ids.Contains(r.Id), ct) != ids.Count)
                return PropertyErrors.RoomNotFound;
        }
        return null;
    }
}

/// <summary>Một dòng = một người ở trong một hợp đồng (người ở nhiều lần ⇒ nhiều dòng).</summary>
internal sealed class RenterExportRow
{
    public Guid PropertyId { get; init; }
    public string PropertyCode { get; init; } = null!;
    public string PropertyName { get; init; } = null!;
    public Guid RoomId { get; init; }
    public string RoomCode { get; init; } = null!;
    public string? Floor { get; init; }
    public string ContractNo { get; init; } = null!;
    public ContractStatus ContractStatus { get; init; }
    public bool IsRepresentative { get; init; }
    public bool IsHouseholdHead { get; init; }
    public string FullName { get; init; } = null!;
    public DateOnly DateOfBirth { get; init; }
    public Gender Gender { get; init; }
    public string? Phone { get; init; }
    public IdDocumentType IdType { get; init; }
    public string IdNumberLast4 { get; init; } = null!;
    public byte[] IdNumberEncrypted { get; init; } = null!;
    public DateOnly? IdIssueDate { get; init; }
    public string? IdIssuePlace { get; init; }
    public string Nationality { get; init; } = null!;
    public string? PermanentAddress { get; init; }
    public string? Occupation { get; init; }
    public string? Workplace { get; init; }
    public string? Relationship { get; init; }
    public OccupantRelationship? RelationshipType { get; init; }
    public bool GuardianConsent { get; init; }
    public DateOnly MoveInDate { get; init; }
    public DateOnly? MoveOutDate { get; init; }
    public string? EmergencyContactName { get; init; }
    public string? EmergencyContactPhone { get; init; }
}

internal static class RenterExportQuery
{
    private static readonly ContractStatus[] LivedStatuses = [ContractStatus.Active, ContractStatus.Liquidating, ContractStatus.Ended];

    /// <summary>Người ở có thời gian ở giao với [from, to] trong HĐ đã bàn giao (nháp / đã hủy không tính).</summary>
    public static IQueryable<RenterExportRow> Build(IAppDbContext db, ExportRentersQuery request, DateOnly fromDate, DateOnly toDate)
    {
        var query =
            from c in db.Contracts
            where LivedStatuses.Contains(c.Status)
            from o in c.Occupants
            // Đang thanh lý: ngày chuyển đi chỉ ghi khi hoàn tất ⇒ dùng ngày trả phòng của HĐ.
            let moveOut = o.MoveOutDate ?? c.ActualEndDate
            where o.MoveInDate <= toDate && (moveOut == null || moveOut >= fromDate)
            join r in db.Renters on o.RenterId equals r.Id
            join room in db.Rooms on c.RoomId equals room.Id
            join p in db.Properties on c.PropertyId equals p.Id
            select new RenterExportRow
            {
                PropertyId = p.Id, PropertyCode = p.Code, PropertyName = p.Name,
                RoomId = room.Id, RoomCode = room.Code, Floor = room.Floor,
                ContractNo = c.ContractNo, ContractStatus = c.Status, IsRepresentative = c.RepresentativeRenterId == r.Id,
                IsHouseholdHead = (c.HouseholdHeadRenterId ?? c.RepresentativeRenterId) == r.Id,
                FullName = r.FullName, DateOfBirth = r.DateOfBirth, Gender = r.Gender, Phone = r.Phone,
                IdType = r.IdType, IdNumberLast4 = r.IdNumberLast4, IdNumberEncrypted = r.IdNumberEncrypted,
                IdIssueDate = r.IdIssueDate, IdIssuePlace = r.IdIssuePlace, Nationality = r.Nationality,
                PermanentAddress = r.PermanentAddress, Occupation = r.Occupation, Workplace = r.Workplace,
                Relationship = o.Relationship, RelationshipType = o.RelationshipType, GuardianConsent = o.GuardianConsent,
                MoveInDate = o.MoveInDate, MoveOutDate = moveOut,
                EmergencyContactName = r.EmergencyContactName, EmergencyContactPhone = r.EmergencyContactPhone
            };

        if (request.PropertyIds is { Count: > 0 } propertyIds)
            query = query.Where(x => propertyIds.Contains(x.PropertyId));
        if (request.RoomIds is { Count: > 0 } roomIds)
            query = query.Where(x => roomIds.Contains(x.RoomId));
        if (request.Floors is { Count: > 0 } floors)
        {
            var normalized = floors.Select(f => f.Trim()).ToList();
            query = query.Where(x => x.Floor != null && normalized.Contains(x.Floor));
        }
        if (request.RoomGroupIds is { Count: > 0 } groupIds)
            query = query.Where(x => db.RoomGroups.Any(g => groupIds.Contains(g.Id) && g.Members.Any(m => m.RoomId == x.RoomId)));

        return query;
    }
}
