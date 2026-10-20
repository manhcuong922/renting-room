using FluentValidation;
using Mediator;
using renting_room.Application.Fees;
using renting_room.Application.Rooms;
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

public sealed class BulkInstallMetersHandler(
    IFeeQueries fees, IRoomQueries rooms, IMeterRepository meterRepository, IMeterQueries meterQueries, IUnitOfWork unitOfWork)
    : IRequestHandler<BulkInstallMetersCommand, Result<BulkInstallResult>>
{
    public async ValueTask<Result<BulkInstallResult>> Handle(BulkInstallMetersCommand request, CancellationToken cancellationToken)
    {
        var fee = await fees.FindTypeAsync(request.FeeTypeId, cancellationToken);
        if (fee is null || fee.PropertyId != request.PropertyId)
            return FeeErrors.NotInProperty;
        if (fee.Group != FeeGroup.Metered)
            return MeterErrors.FeeNotMetered;
        if (fee.IsArchived)
            return FeeErrors.Archived;

        var roomIds = request.Rows.Select(r => r.RoomId).ToList();
        var roomsById = await rooms.ListInPropertyAsync(request.PropertyId, roomIds, cancellationToken);
        var hasMeter = await meterQueries.GetMeteredRoomIdsAsync(roomIds, fee.Id, cancellationToken);

        var errors = new List<BulkInstallRowError>();
        var meters = new List<Meter>();
        for (var index = 0; index < request.Rows.Count; index++)
        {
            var row = request.Rows[index];
            var error = !roomsById.TryGetValue(row.RoomId, out var room) ? PropertyErrors.RoomNotFound
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

        meterRepository.AddRange(meters);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return new BulkInstallResult(meters.Count, meters.Select(m => m.Id).ToList());
    }
}
