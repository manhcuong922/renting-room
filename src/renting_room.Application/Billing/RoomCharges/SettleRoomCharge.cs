using FluentValidation;
using Mediator;
using renting_room.Application.Common.Interfaces;
using renting_room.Domain.Common;
using renting_room.Domain.Contracts;

namespace renting_room.Application.Billing;

// Đánh dấu đã thanh toán / bỏ đánh dấu / hủy
public sealed record SettleRoomChargeCommand(Guid Id, DateOnly SettledOn, PaymentMethod Method) : IRequest<Result<RoomChargeDto>>;

public sealed class SettleRoomChargeCommandValidator : AbstractValidator<SettleRoomChargeCommand>
{
    public SettleRoomChargeCommandValidator() => RuleFor(x => x.Method).IsInEnum();
}

/// <summary>BL-UC-16: đánh dấu khoản đã thanh toán / đã hoàn ngay ⇒ dòng trên nháp (nếu có) ra khỏi tổng.</summary>
public sealed class SettleRoomChargeHandler(IAppDbContext db, TimeProvider clock) : IRequestHandler<SettleRoomChargeCommand, Result<RoomChargeDto>>
{
    public async ValueTask<Result<RoomChargeDto>> Handle(SettleRoomChargeCommand request, CancellationToken cancellationToken) =>
        await RoomChargeMutation.RunAsync(db, request.Id, (charge, draft) =>
        {
            var settled = charge.Settle(request.SettledOn, request.Method, clock.GetUtcNow().ToBusinessDate());
            return settled.IsFailure || draft is null ? settled : draft.SetChargeSettled(charge.Id, true);
        }, cancellationToken);
}
