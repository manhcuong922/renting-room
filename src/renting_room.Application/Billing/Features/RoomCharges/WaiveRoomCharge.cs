using Mediator;
using renting_room.Application.Contracts;
using renting_room.Application.Meters;
using renting_room.Domain.Billing;
using renting_room.Domain.Common;
using renting_room.Domain.Contracts;

namespace renting_room.Application.Billing;

/// <summary>M06 MT-BR-19 "Sau thanh lý": "Không trả lại" khoản Chênh lệch sau thanh lý đang chờ — khoản hủy, báo cáo không đổi.</summary>
public sealed record WaiveRoomChargeCommand(Guid Id) : IRequest<Result<RoomChargeDto>>;

public sealed class WaiveRoomChargeHandler(IRoomChargeQueries chargeQueries, IContractQueries contracts, RoomChargeBilling roomCharges, TimeProvider clock)
    : IRequestHandler<WaiveRoomChargeCommand, Result<RoomChargeDto>>
{
    public async ValueTask<Result<RoomChargeDto>> Handle(WaiveRoomChargeCommand request, CancellationToken cancellationToken)
    {
        var contractId = await chargeQueries.GetContractIdAsync(request.Id, cancellationToken);
        if (contractId is null)
            return RoomChargeErrors.NotFound;
        var ended = await contracts.GetStatusAsync(contractId.Value, cancellationToken) == ContractStatus.Ended;
        return await roomCharges.MutateAsync(request.Id, (charge, _) =>
            ended && charge is { IsReadingAdjustment: true, IsPending: true, IsSettled: false }
                ? charge.Cancel(AfterEndAdjustment.NotReturnedReason, clock.GetUtcNow())
                : Result.Failure(RoomChargeErrors.NotWaivable), cancellationToken);
    }
}
