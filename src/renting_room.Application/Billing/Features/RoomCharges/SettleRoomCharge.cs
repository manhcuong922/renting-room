using FluentValidation;
using Mediator;
using renting_room.Domain.Common;
using renting_room.Domain.Contracts;

namespace renting_room.Application.Billing;

public sealed record SettleRoomChargeCommand(Guid Id, DateOnly SettledOn, PaymentMethod Method) : IRequest<Result<RoomChargeDto>>;

public sealed class SettleRoomChargeCommandValidator : AbstractValidator<SettleRoomChargeCommand>
{
    public SettleRoomChargeCommandValidator() => RuleFor(x => x.Method).IsInEnum();
}

public sealed class SettleRoomChargeHandler(RoomChargeBilling roomCharges, TimeProvider clock)
    : IRequestHandler<SettleRoomChargeCommand, Result<RoomChargeDto>>
{
    public async ValueTask<Result<RoomChargeDto>> Handle(SettleRoomChargeCommand request, CancellationToken cancellationToken) =>
        await roomCharges.MutateAsync(request.Id, (charge, draft) =>
        {
            var settled = charge.Settle(request.SettledOn, request.Method, clock.GetUtcNow().ToBusinessDate());
            return settled.IsFailure || draft is null ? settled : draft.SetChargeSettled(charge.Id, true);
        }, cancellationToken);
}
