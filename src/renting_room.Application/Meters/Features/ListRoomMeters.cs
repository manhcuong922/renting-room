using Mediator;
using renting_room.Application.Fees;
using renting_room.Application.Rooms;
using renting_room.Domain.Common;
using renting_room.Domain.Properties;

namespace renting_room.Application.Meters;

public sealed record ListRoomMetersQuery(Guid RoomId, bool IncludeRemoved) : IRequest<Result<IReadOnlyList<MeterDto>>>;

public sealed class ListRoomMetersHandler(IRoomQueries rooms, IMeterQueries meterQueries, IFeeQueries feeQueries)
    : IRequestHandler<ListRoomMetersQuery, Result<IReadOnlyList<MeterDto>>>
{
    public async ValueTask<Result<IReadOnlyList<MeterDto>>> Handle(ListRoomMetersQuery request, CancellationToken cancellationToken)
    {
        if (!await rooms.ExistsAsync(request.RoomId, cancellationToken))
            return PropertyErrors.RoomNotFound;

        var meters = await meterQueries.ListByRoomAsync(request.RoomId, request.IncludeRemoved, cancellationToken);
        var feeIds = meters.Select(m => m.FeeTypeId).Distinct().ToList();
        var fees = await feeQueries.GetTypesAsync(feeIds, cancellationToken);

        return meters
            .OrderBy(m => fees.GetValueOrDefault(m.FeeTypeId)?.SortOrder).ThenByDescending(m => m.InstalledDate)
            .Select(m => m.ToDto(fees.GetValueOrDefault(m.FeeTypeId)))
            .ToList();
    }
}
