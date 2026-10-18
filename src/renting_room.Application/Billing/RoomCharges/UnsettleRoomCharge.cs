using FluentValidation;
using Mediator;
using Microsoft.EntityFrameworkCore;
using renting_room.Application.Common.Interfaces;
using renting_room.Domain.Billing;
using renting_room.Domain.Common;

namespace renting_room.Application.Billing;

public sealed record UnsettleRoomChargeCommand(Guid Id) : IRequest<Result<RoomChargeDto>>;

/// <summary>Bỏ đánh dấu đã thanh toán (nhập nhầm) ⇒ dòng trên nháp tính lại vào tổng.</summary>
public sealed class UnsettleRoomChargeHandler(IAppDbContext db) : IRequestHandler<UnsettleRoomChargeCommand, Result<RoomChargeDto>>
{
    public async ValueTask<Result<RoomChargeDto>> Handle(UnsettleRoomChargeCommand request, CancellationToken cancellationToken)
    {
        // BL-BR-30: bỏ đánh dấu một khoản bù ⇒ khoản đó quay lại chờ trừ vào phiếu, phải còn trong giới hạn 1 tháng tiền phòng.
        var credit = await db.RoomCharges.AsNoTracking()
            .Where(c => c.Id == request.Id && c.Kind == RoomChargeKind.Credit && c.IsSettled && c.CancelledAt == null)
            .Select(c => new { c.ContractId, c.IncurredOn, c.Amount }).FirstOrDefaultAsync(cancellationToken);
        if (credit is not null && await CreateRoomChargeHandler.CreditLimitExceededAsync(
                db, credit.ContractId, credit.IncurredOn, credit.Amount, cancellationToken) is { } limit)
            return RoomChargeErrors.CreditTooHigh(limit);

        return await RoomChargeMutation.RunAsync(db, request.Id, (charge, draft) =>
        {
            var unsettled = charge.Unsettle();
            return unsettled.IsFailure || draft is null ? unsettled : draft.SetChargeSettled(charge.Id, false);
        }, cancellationToken);
    }
}
