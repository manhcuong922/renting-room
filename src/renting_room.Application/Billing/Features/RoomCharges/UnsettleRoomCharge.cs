using Mediator;
using renting_room.Application.Contracts;
using renting_room.Domain.Billing;
using renting_room.Domain.Common;
using renting_room.Domain.Contracts;

namespace renting_room.Application.Billing;

public sealed record UnsettleRoomChargeCommand(Guid Id) : IRequest<Result<RoomChargeDto>>;

public sealed class UnsettleRoomChargeHandler(RoomChargeBilling roomCharges, IRoomChargeQueries roomChargeQueries, IContractQueries contracts)
    : IRequestHandler<UnsettleRoomChargeCommand, Result<RoomChargeDto>>
{
    public async ValueTask<Result<RoomChargeDto>> Handle(UnsettleRoomChargeCommand request, CancellationToken cancellationToken)
    {
        var credit = await roomChargeQueries.FindSettledCreditAsync(request.Id, cancellationToken);
        if (credit is not null && await CreateRoomChargeHandler.CreditLimitExceededAsync(
                contracts, roomChargeQueries, credit.ContractId, credit.IncurredOn, credit.Amount, cancellationToken) is { } limit)
            return RoomChargeErrors.CreditTooHigh(limit);

        // HĐ đã kết thúc: khoản bỏ đánh dấu sẽ treo (không còn hóa đơn / màn hoàn tất) — chỉ cho sửa nhầm "Chênh lệch sau thanh lý" (M07 BL-BR-31).
        var contractId = await roomChargeQueries.GetContractIdAsync(request.Id, cancellationToken);
        var ended = contractId is { } id && await contracts.GetStatusAsync(id, cancellationToken) == ContractStatus.Ended;
        return await roomCharges.MutateAsync(request.Id, (charge, draft) =>
        {
            if (ended && !charge.IsReadingAdjustment)
                return Result.Failure(RoomChargeErrors.NoOpenInvoice);
            var unsettled = charge.Unsettle();
            return unsettled.IsFailure || draft is null ? unsettled : draft.SetChargeSettled(charge.Id, false);
        }, cancellationToken);
    }
}
