using FluentValidation;
using Mediator;
using Microsoft.EntityFrameworkCore;
using renting_room.Application.Common.Interfaces;
using renting_room.Application.Common.Validation;
using renting_room.Domain.Common;
using renting_room.Domain.Fees;
using renting_room.Domain.Meters;
using renting_room.Domain.Properties;

namespace renting_room.Application.Meters;

// ============================================================ MT-UC-01: lắp công tơ hàng loạt cho nhiều phòng của khu

public sealed record BulkInstallRow(Guid RoomId, string? SerialNo, decimal InitialValue);

public sealed record BulkInstallRowError(int Index, Guid RoomId, string Code, string Message);

public sealed record BulkInstallResult(int Installed, IReadOnlyList<Guid> MeterIds);

public sealed record BulkInstallMetersCommand(Guid PropertyId, Guid FeeTypeId, DateOnly InstalledDate, IReadOnlyList<BulkInstallRow> Rows)
    : IRequest<Result<BulkInstallResult>>;

public sealed class BulkInstallMetersCommandValidator : AbstractValidator<BulkInstallMetersCommand>
{
    public BulkInstallMetersCommandValidator(TimeProvider clock)
    {
        RuleFor(x => x.FeeTypeId).NotEmpty().WithErrorCode("REQUIRED");
        RuleFor(x => x.InstalledDate).ReadingDate(clock);
        RuleFor(x => x.Rows).NotEmpty().Must(r => r.Count <= 500).WithErrorCode("OUT_OF_RANGE")
            .Must(r => r.Select(x => x.RoomId).Distinct().Count() == r.Count).WithErrorCode("DUPLICATE_ROOM").WithMessage("Mỗi phòng chỉ 1 dòng.");
        RuleForEach(x => x.Rows).ChildRules(row =>
        {
            row.RuleFor(r => r.InitialValue).ReadingValue();
            row.RuleFor(r => r.SerialNo).OptionalText(50);
        });
    }
}

/// <summary>Mỗi dòng = đúng lệnh lắp công tơ đơn (MT-BR-01/02); kiểm hết các dòng — lỗi bất kỳ ⇒ không lưu dòng nào (MT-BR-07).</summary>
public sealed class BulkInstallMetersHandler(IAppDbContext db) : IRequestHandler<BulkInstallMetersCommand, Result<BulkInstallResult>>
{
    public async ValueTask<Result<BulkInstallResult>> Handle(BulkInstallMetersCommand request, CancellationToken cancellationToken)
    {
        var fee = await db.FeeTypes.AsNoTracking().FirstOrDefaultAsync(f => f.Id == request.FeeTypeId, cancellationToken);
        if (fee is null || fee.PropertyId != request.PropertyId)
            return FeeErrors.NotInProperty;
        if (fee.Group != FeeGroup.Metered)
            return MeterErrors.FeeNotMetered;
        if (fee.IsArchived)
            return FeeErrors.Archived;

        var roomIds = request.Rows.Select(r => r.RoomId).ToList();
        var rooms = await db.Rooms.AsNoTracking().Where(r => roomIds.Contains(r.Id) && r.PropertyId == request.PropertyId)
            .ToDictionaryAsync(r => r.Id, cancellationToken);
        var hasMeter = (await db.Meters.Where(m => roomIds.Contains(m.RoomId) && m.FeeTypeId == fee.Id && m.RemovedDate == null)
            .Select(m => m.RoomId).ToListAsync(cancellationToken)).ToHashSet();

        var errors = new List<BulkInstallRowError>();
        var meters = new List<Meter>();
        for (var index = 0; index < request.Rows.Count; index++)
        {
            var row = request.Rows[index];
            var error = !rooms.TryGetValue(row.RoomId, out var room) ? PropertyErrors.RoomNotFound
                : room.IsArchived ? PropertyErrors.RoomArchived
                : hasMeter.Contains(row.RoomId) ? MeterErrors.AlreadyActive
                : null;
            if (error is not null)
                errors.Add(new BulkInstallRowError(index, row.RoomId, error.Code, error.Message));
            else
                meters.Add(Meter.Install(request.PropertyId, row.RoomId, fee.Id, row.SerialNo, request.InstalledDate, row.InitialValue, null));
        }
        if (errors.Count > 0)
            return Error.BusinessRule("BULK_INSTALL_INVALID", "Có dòng lắp công tơ không hợp lệ — chưa lưu dòng nào.").WithDetail("rowErrors", errors);

        db.Meters.AddRange(meters);
        await db.SaveChangesAsync(cancellationToken);
        return new BulkInstallResult(meters.Count, meters.Select(m => m.Id).ToList());
    }
}

// ============================================================ MT-UC-01: lưới lắp hàng loạt — phòng chưa có công tơ của khoản thu

public sealed record InstallSheetRowDto(Guid RoomId, string RoomCode, string? Floor);

public sealed record GetInstallSheetQuery(Guid PropertyId, Guid FeeTypeId) : IRequest<Result<IReadOnlyList<InstallSheetRowDto>>>;

/// <summary>Các phòng đang dùng của khu chưa có công tơ đang hoạt động của khoản thu — đầu vào cho lệnh lắp hàng loạt.</summary>
public sealed class GetInstallSheetHandler(IAppDbContext db) : IRequestHandler<GetInstallSheetQuery, Result<IReadOnlyList<InstallSheetRowDto>>>
{
    public async ValueTask<Result<IReadOnlyList<InstallSheetRowDto>>> Handle(GetInstallSheetQuery request, CancellationToken cancellationToken)
    {
        var fee = await db.FeeTypes.AsNoTracking().FirstOrDefaultAsync(f => f.Id == request.FeeTypeId, cancellationToken);
        if (fee is null || fee.PropertyId != request.PropertyId)
            return FeeErrors.NotInProperty;
        if (fee.Group != FeeGroup.Metered)
            return MeterErrors.FeeNotMetered;

        var metered = (await db.Meters.Where(m => m.PropertyId == request.PropertyId && m.FeeTypeId == fee.Id && m.RemovedDate == null)
            .Select(m => m.RoomId).ToListAsync(cancellationToken)).ToHashSet();
        var rooms = await db.Rooms.AsNoTracking().Where(r => r.PropertyId == request.PropertyId).ToListAsync(cancellationToken);
        return rooms.Where(r => !r.IsArchived && !metered.Contains(r.Id))
            .OrderBy(r => r.Floor).ThenBy(r => r.Code)
            .Select(r => new InstallSheetRowDto(r.Id, r.Code, r.Floor)).ToList();
    }
}

// ============================================================ MT-BR-14: sản lượng phòng trống (không tính cho người thuê nào)

/// <summary>Một khoảng công tơ chạy khi phòng không có người thuê: từ chỉ số liền trước tới chỉ số nhận phòng của HĐ kế tiếp.</summary>
public sealed record VacantUsageRow(
    Guid RoomId, string RoomCode, Guid MeterId, string? SerialNo, Guid FeeTypeId, string FeeTypeName, string Unit, DateOnly FromDate,
    decimal FromValue, DateOnly ToDate, decimal ToValue, decimal Quantity);

public sealed record VacantUsageTotal(Guid FeeTypeId, string FeeTypeName, string Unit, decimal Quantity);

public sealed record VacantUsageDto(DateOnly From, DateOnly To, IReadOnlyList<VacantUsageRow> Rows, IReadOnlyList<VacantUsageTotal> Totals);

/// <param name="From">Mặc định 12 tháng trước hôm nay.</param>
public sealed record GetVacantUsageQuery(Guid PropertyId, DateOnly? From, DateOnly? To) : IRequest<Result<VacantUsageDto>>;

/// <summary>
/// MT-BR-14: mỗi chỉ số nhận phòng cao hơn chỉ số liền trước trên cùng công tơ là phần điện nước dùng lúc phòng trống (sửa chữa, thử phòng) —
/// không nằm trên phiếu của ai. Tổng hợp theo ngày nhận phòng để chủ trọ đối chiếu hóa đơn điện nước của khu.
/// </summary>
public sealed class GetVacantUsageHandler(IAppDbContext db, TimeProvider clock) : IRequestHandler<GetVacantUsageQuery, Result<VacantUsageDto>>
{
    public async ValueTask<Result<VacantUsageDto>> Handle(GetVacantUsageQuery request, CancellationToken cancellationToken)
    {
        if (!await db.Properties.AnyAsync(p => p.Id == request.PropertyId, cancellationToken))
            return PropertyErrors.PropertyNotFound;

        var to = request.To ?? clock.GetUtcNow().ToBusinessDate();
        var from = request.From ?? to.AddYears(-1);
        var meters = await db.Meters.AsNoTracking().Include(m => m.Readings).Where(m => m.PropertyId == request.PropertyId).ToListAsync(cancellationToken);
        var roomIds = meters.Select(m => m.RoomId).Distinct().ToList();
        var feeIds = meters.Select(m => m.FeeTypeId).Distinct().ToList();
        var rooms = await db.Rooms.AsNoTracking().Where(r => roomIds.Contains(r.Id)).ToDictionaryAsync(r => r.Id, r => r.Code, cancellationToken);
        var fees = await db.FeeTypes.AsNoTracking().Where(f => feeIds.Contains(f.Id)).ToDictionaryAsync(f => f.Id, cancellationToken);

        var rows = new List<VacantUsageRow>();
        foreach (var meter in meters)
        {
            var ordered = meter.Ordered.ToList();
            for (var i = 1; i < ordered.Count; i++)
            {
                var (previous, handover) = (ordered[i - 1], ordered[i]);
                if (handover.Kind != ReadingKind.Handover || handover.Value <= previous.Value || handover.ReadingDate < from || handover.ReadingDate > to)
                    continue;
                var fee = fees[meter.FeeTypeId];
                rows.Add(new VacantUsageRow(meter.RoomId, rooms.GetValueOrDefault(meter.RoomId, string.Empty), meter.Id, meter.SerialNo, fee.Id, fee.Name,
                    fee.Unit, previous.ReadingDate, previous.Value, handover.ReadingDate, handover.Value, handover.Value - previous.Value));
            }
        }
        var totals = rows.GroupBy(r => r.FeeTypeId)
            .Select(g => new VacantUsageTotal(g.Key, g.First().FeeTypeName, g.First().Unit, g.Sum(r => r.Quantity))).OrderBy(t => t.FeeTypeName).ToList();
        return new VacantUsageDto(from, to, rows.OrderBy(r => r.RoomCode).ThenBy(r => r.ToDate).ToList(), totals);
    }
}
