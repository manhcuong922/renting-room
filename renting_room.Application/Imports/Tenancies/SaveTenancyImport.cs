using FluentValidation;
using Mediator;
using Microsoft.EntityFrameworkCore;
using renting_room.Application.Common.Interfaces;
using renting_room.Application.Contracts;
using renting_room.Application.Meters;
using renting_room.Application.Renters;
using renting_room.Domain.Common;
using renting_room.Domain.Contracts;

namespace renting_room.Application.Imports;

/// <summary>
/// RT-UC-10 bước 2: nhận các dòng (đã sửa trên màn), kiểm lại toàn bộ, lưu các phòng hợp lệ — mỗi phòng 1 transaction: dùng lại / tạo người
/// thuê → HĐ (tính tiền từ đầu kỳ hiện tại của khu hoặc ngày vào ở, thiếu bản ký) → kích hoạt (chỉ số nhận phòng = số mới nhất).
/// </summary>
public sealed record SaveTenancyImportCommand(Guid PropertyId, IReadOnlyList<TenancyImportRow> Rows) : IRequest<Result<ImportSaveResultDto>>;

public sealed class SaveTenancyImportCommandValidator : AbstractValidator<SaveTenancyImportCommand>
{
    public SaveTenancyImportCommandValidator() =>
        RuleFor(x => x.Rows).NotEmpty().WithErrorCode("REQUIRED").Must(r => r is null || r.Count <= ImportErrors.MaxRows).WithErrorCode("IMPORT_TOO_MANY_ROWS");
}

public sealed class SaveTenancyImportHandler(
    IAppDbContext db, ICurrentUser currentUser, IPersonalDataProtector protector, ISender sender, IAuditTrail auditTrail,
    IValidator<CreateRenterCommand> renterValidator, TimeProvider clock)
    : IRequestHandler<SaveTenancyImportCommand, Result<ImportSaveResultDto>>
{
    public async ValueTask<Result<ImportSaveResultDto>> Handle(SaveTenancyImportCommand request, CancellationToken cancellationToken)
    {
        var property = await ImportProperty.LoadAsync(db, request.PropertyId, cancellationToken);
        if (property.IsFailure)
            return property.Error!;
        var check = await new TenancyImportChecker(db, currentUser, protector, renterValidator)
            .CheckAsync(property.Value!, request.Rows, new Dictionary<int, List<ImportIssue>>(), clock.GetUtcNow().ToBusinessDate(), cancellationToken);

        var units = new List<ImportUnitResult>();
        foreach (var room in check.Rooms)
        {
            if (!room.IsValid)
            {
                var rowErrors = check.Rows.Where(r => room.RowNumbers.Contains(r.Row.RowNumber)).SelectMany(r => r.Errors);
                units.Add(new ImportUnitResult(room.RoomCode, room.RowNumbers, ImportOutcome.Invalid, [.. room.Errors, .. rowErrors]));
                continue;
            }
            var plan = check.Plans.Single(p => p.RoomCode == room.RoomCode);
            var failure = await ImportFiles.SaveUnitAsync(db, () => SaveAsync(plan, cancellationToken), cancellationToken);
            units.Add(new ImportUnitResult(room.RoomCode, room.RowNumbers, failure.Count == 0 ? ImportOutcome.Saved : ImportOutcome.Failed, failure));
        }
        // Dòng thiếu mã phòng không thuộc phòng nào.
        foreach (var orphan in check.Rows.Where(r => r.Row.RoomCode is null))
            units.Add(new ImportUnitResult($"#{orphan.Row.RowNumber}", [orphan.Row.RowNumber], ImportOutcome.Invalid, orphan.Errors));
        return await ImportPreview.RecordAsync(db, auditTrail, request.PropertyId, "Tenancies", units, cancellationToken);
    }

    private async Task<Error?> SaveAsync(TenancyPlan plan, CancellationToken ct)
    {
        var ids = new Dictionary<int, Guid>();
        foreach (var row in plan.Rows)
        {
            var id = await ResolveRenterAsync(row, ct);
            if (id.IsFailure)
                return id.Error;
            ids[row.RowNumber] = id.Value;
        }

        // Nhiều dòng "Đứng tên": dòng đầu đứng tên HĐ, các dòng sau là đồng thuê.
        var representative = plan.Rows.Where(r => TenancyImportTemplate.IsRepresentative(r.Role)).MinBy(r => r.RowNumber)!;
        bool IsCoTenant(TenancyImportRow r) => TenancyImportTemplate.IsRepresentative(r.Role) && r.RowNumber != representative.RowNumber;
        var occupants = plan.Rows.Where(r => TenancyImportTemplate.IsLiving(r.Role))
            .Select(r => new OccupantRequest(ids[r.RowNumber], r.MoveInDate, null, null, null,
                r.Role == ImportRole.Occupant ? r.Relationship : IsCoTenant(r) ? OccupantRelationship.CoTenant : null,
                Role: IsCoTenant(r) ? OccupantRole.CoTenant : null))
            .ToList();
        var input = new ContractInput(ids[representative.RowNumber], plan.StartDate, null, null, null, null, plan.MonthlyRent, plan.DepositAmount, null,
            plan.BillingStartDate == plan.StartDate ? null : plan.BillingStartDate, null, null, null, null, "Nhập từ Excel", occupants);
        var created = await sender.Send(new CreateContractCommand(plan.RoomId, null, input), ct);
        if (created.IsFailure)
            return created.Error;

        // Chỉ số nhận phòng = số mới nhất của từng công tơ đang đo tại ngày tính tiền (chỉ số khai ở form phòng).
        var meters = await db.Meters.AsNoTracking()
            .Where(m => m.RoomId == plan.RoomId && m.InstalledDate <= plan.BillingStartDate && (m.RemovedDate == null || m.RemovedDate > plan.BillingStartDate))
            .Select(m => m.Id).ToListAsync(ct);
        var activated = await sender.Send(new ActivateContractCommand(created.Value!.Id, meters.Select(m => new MeterReadingInput(m, null)).ToList()), ct);
        return activated.IsFailure ? activated.Error : null;
    }

    /// <summary>Có số giấy tờ trùng hồ sơ ⇒ dùng lại (kể cả hồ sơ vừa tạo ở phòng trước của lần import này); không thì tạo mới.</summary>
    private async Task<Result<Guid>> ResolveRenterAsync(TenancyImportRow row, CancellationToken ct)
    {
        if (row is { IdType: { } type, IdNumber: { } number })
        {
            var hash = protector.ProtectIdNumber(currentUser.OrganizationId!.Value, type, number).Hash;
            if (await db.Renters.Where(r => r.IdNumberHash == hash).Select(r => (Guid?)r.Id).FirstOrDefaultAsync(ct) is { } existing)
                return existing;
        }
        return await sender.Send(new CreateRenterCommand(TenancyImportTemplate.Renter(row)), ct);
    }
}
