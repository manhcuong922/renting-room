using FluentValidation;
using Mediator;

namespace renting_room.Application.Billing;

public sealed record ListRoomChargesQuery(Guid? RoomId, Guid? PropertyId, RoomChargeStatus? Status, DateOnly? From, DateOnly? To)
    : IRequest<IReadOnlyList<RoomChargeDto>>;

/// <summary>BL-UC-17: khoản phát sinh theo phòng / khu, mới nhất trước — chủ trọ ở xa xem người quản lý đã ghi gì.</summary>
public sealed class ListRoomChargesHandler(IRoomChargeQueries roomChargeQueries, RoomChargeBilling roomCharges)
    : IRequestHandler<ListRoomChargesQuery, IReadOnlyList<RoomChargeDto>>
{
    private const int MaxRows = 500;

    public async ValueTask<IReadOnlyList<RoomChargeDto>> Handle(ListRoomChargesQuery request, CancellationToken cancellationToken)
    {
        var charges = await roomChargeQueries.ListAsync(request, MaxRows, cancellationToken);
        return await roomCharges.ToDtosAsync(charges, cancellationToken);
    }
}
