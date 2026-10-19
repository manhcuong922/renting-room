using FluentValidation;
using Mediator;
using renting_room.Application.Billing;
using renting_room.Application.Contracts;
using renting_room.Application.Common.Interfaces;
using renting_room.Application.Common.Validation;
using renting_room.Domain.Common;
using renting_room.Domain.Meters;

namespace renting_room.Application.Meters;

public sealed record MeterRemovedDto(IReadOnlyList<Warning> Warnings);

public sealed record RemoveMeterCommand(Guid MeterId, DateOnly Date, decimal FinalValue, string? Note) : IRequest<Result<MeterRemovedDto>>;

public sealed class RemoveMeterCommandValidator : AbstractValidator<RemoveMeterCommand>
{
    public RemoveMeterCommandValidator(TimeProvider clock)
    {
        RuleFor(x => x.Date).ReadingDate(clock);
        RuleFor(x => x.FinalValue).ReadingValue();
        RuleFor(x => x.Note).OptionalText(300);
    }
}

/// <summary>MT-UC-03 / MT-BR-10: gỡ công tơ (không thay) — bắt buộc số cuối; phòng đang có HĐ ⇒ cảnh báo.</summary>
public sealed class RemoveMeterHandler(IMeterRepository meters, IInvoiceQueries invoices, IContractQueries contracts, IUnitOfWork unitOfWork)
    : IRequestHandler<RemoveMeterCommand, Result<MeterRemovedDto>>
{
    public async ValueTask<Result<MeterRemovedDto>> Handle(RemoveMeterCommand request, CancellationToken cancellationToken)
    {
        await using var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken);
        var meter = (await meters.LockAndLoadAsync([request.MeterId], cancellationToken)).FirstOrDefault();
        if (meter is null)
            return MeterErrors.NotFound;
        if (MeterPeriodLocks.Check(await invoices.GetMeterLockedUntilAsync([meter.Id], cancellationToken), meter.Id, request.Date) is { } locked)
            return locked;

        var removed = meter.Remove(request.Date, request.FinalValue, request.Note);
        if (removed.IsFailure)
            return removed.Error!;

        await unitOfWork.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        var hasContract = await contracts.AnyActiveInRoomAsync(meter.RoomId, cancellationToken);
        return new MeterRemovedDto(hasContract
            ? [new Warning("ROOM_HAS_OPEN_CONTRACT", "Phòng đang có người thuê — từ kỳ sau phiếu không còn dòng của khoản này.")]
            : []);
    }
}
