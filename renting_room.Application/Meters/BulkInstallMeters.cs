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
