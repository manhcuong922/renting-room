using FluentValidation;
using Mediator;
using renting_room.Application.Common.Interfaces;
using renting_room.Application.Common.Validation;
using renting_room.Domain.Billing;
using renting_room.Domain.Common;

namespace renting_room.Application.Billing;

public sealed record CancelRoomChargeCommand(Guid Id, string Reason) : IRequest<Result<RoomChargeDto>>;

public sealed class CancelRoomChargeCommandValidator : AbstractValidator<CancelRoomChargeCommand>
{
    public CancelRoomChargeCommandValidator() => RuleFor(x => x.Reason).RequiredText(300, "Lý do hủy");
}

/// <summary>BL-BR-35: hủy khoản chưa nằm trên phiếu đã chốt; đang trên nháp ⇒ xóa dòng.</summary>
public sealed class CancelRoomChargeHandler(IAppDbContext db, TimeProvider clock) : IRequestHandler<CancelRoomChargeCommand, Result<RoomChargeDto>>
{
    public async ValueTask<Result<RoomChargeDto>> Handle(CancelRoomChargeCommand request, CancellationToken cancellationToken) =>
        await RoomChargeMutation.RunAsync(db, request.Id, (charge, draft) =>
        {
            if (charge.IsSystem)
                return Result.Failure(RoomChargeErrors.System);
            var removed = draft?.RemoveChargeLine(charge.Id) ?? Result.Success();
            return removed.IsFailure ? removed : charge.Cancel(request.Reason, clock.GetUtcNow());
        }, cancellationToken);
}
