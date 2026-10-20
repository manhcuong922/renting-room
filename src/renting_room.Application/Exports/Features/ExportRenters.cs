using System.Data;
using FluentValidation;
using Mediator;
using renting_room.Application.Common.Interfaces;
using renting_room.Application.Common.Security;
using renting_room.Application.Common.Validation;
using renting_room.Domain.Common;
using renting_room.Domain.Identity;
using renting_room.Domain.Contracts;
using renting_room.Domain.Properties;
using renting_room.Domain.Renters;
using renting_room.Application.Identity;
using renting_room.Application.Properties;
using renting_room.Application.Rooms;

namespace renting_room.Application.Exports;

public enum RenterExportLayout
{
    SheetPerProperty, // mỗi khu 1 sheet (mặc định)
    SheetPerFloor,    // mỗi tầng của mỗi khu 1 sheet
    SingleSheet       // gộp 1 sheet
}

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

public sealed class ExportRentersHandler(
    IExportQueries exports,
    IPropertyQueries properties,
    IRoomQueries rooms,
    IUnitOfWork unitOfWork,
    IUserQueries users,
    ICurrentUser currentUser,
    IPersonalDataProtector protector,
    ISpreadsheetWriter writer,
    TimeProvider clock,
    IAuditTrail auditTrail)
    : IRequestHandler<ExportRentersQuery, Result<ExportFile>>
{
    public async ValueTask<Result<ExportFile>> Handle(ExportRentersQuery request, CancellationToken cancellationToken)
    {
        var missing = await FindMissingReferenceAsync(request, cancellationToken);
        if (missing is not null)
            return missing;

        if (request.IncludeSensitive && !await SensitiveDataAccess.CanViewAsync(users, currentUser, cancellationToken))
            return IdentityErrors.SensitiveDataForbidden;

        var now = clock.GetUtcNow();
        var today = now.ToBusinessDate();
        var from = request.FromDate ?? request.ToDate ?? today;
        var to = request.ToDate ?? request.FromDate ?? today;

        await using var snapshot = await unitOfWork.BeginTransactionAsync(IsolationLevel.RepeatableRead, cancellationToken);
        if (await exports.CountRentersAsync(request, from, to, cancellationToken) > ExportErrors.MaxRows)
            return ExportErrors.TooLarge;
        var loaded = await exports.ListRentersAsync(request, from, to, cancellationToken);
        await snapshot.CommitAsync(cancellationToken);

        // Trong mỗi hợp đồng: chủ hộ trước, rồi người đứng tên, sau đó theo thứ tự quan hệ như sổ hộ (vợ/chồng → cha mẹ → con → …).
        var rows = loaded
            .OrderBy(r => r.PropertyCode, StringComparer.Ordinal).ThenBy(r => r.Floor, StringComparer.Ordinal)
            .ThenBy(r => r.RoomCode, StringComparer.Ordinal).ThenBy(r => r.ContractNo, StringComparer.Ordinal)
            .ThenByDescending(r => r.IsHouseholdHead).ThenByDescending(r => r.IsRepresentative)
            .ThenBy(r => r.RelationshipType ?? (OccupantRelationship)int.MaxValue)
            .ThenBy(r => r.FullName, StringComparer.Ordinal)
            .ToList();

        if (request.IncludeSensitive)
            auditTrail.RecordRead(AuditActions.ExportWithIdNumbers, nameof(Renter), null,
                new { rowCount = rows.Count, propertyIds = request.PropertyIds is { Count: > 0 } ids ? ids : null, from, to });

        var subtitle = RenterExportSheets.Subtitle(from, to, today, now, request.IncludeSensitive);
        var idNumber = (RenterExportRow r) => r.IdNumberEncrypted is not { Length: > 0 } ? string.Empty
            : request.IncludeSensitive ? protector.Decrypt(r.IdNumberEncrypted)
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
            if (await properties.CountAsync(ids, ct) != ids.Count)
                return PropertyErrors.PropertyNotFound;
        }
        if (request.RoomGroupIds is { Count: > 0 } groupIds)
        {
            var ids = groupIds.Distinct().ToList();
            if (await rooms.CountGroupsAsync(ids, ct) != ids.Count)
                return PropertyErrors.RoomGroupNotFound;
        }
        if (request.RoomIds is { Count: > 0 } roomIds)
        {
            var ids = roomIds.Distinct().ToList();
            if (await rooms.CountAsync(ids, ct) != ids.Count)
                return PropertyErrors.RoomNotFound;
        }
        return null;
    }
}

public sealed class RenterExportRow
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
    public bool IsCoTenant { get; init; }
    public bool IsHouseholdHead { get; init; }
    public string FullName { get; init; } = null!;
    public DateOnly DateOfBirth { get; init; }
    public Gender Gender { get; init; }
    public string? Phone { get; init; }
    public IdDocumentType? IdType { get; init; }
    public string? IdNumberLast4 { get; init; }
    public byte[]? IdNumberEncrypted { get; init; }
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
