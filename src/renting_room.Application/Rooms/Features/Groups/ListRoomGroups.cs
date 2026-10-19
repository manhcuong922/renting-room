using Mediator;
using renting_room.Domain.Common;
using renting_room.Domain.Properties;
using renting_room.Application.Properties;

namespace renting_room.Application.Rooms;

public sealed record ListRoomGroupsQuery(Guid PropertyId) : IRequest<Result<IReadOnlyList<RoomGroupDto>>>;

public sealed class ListRoomGroupsHandler(IPropertyQueries properties, IRoomQueries rooms) : IRequestHandler<ListRoomGroupsQuery, Result<IReadOnlyList<RoomGroupDto>>>
{
    public async ValueTask<Result<IReadOnlyList<RoomGroupDto>>> Handle(ListRoomGroupsQuery request, CancellationToken cancellationToken)
    {
        if (!await properties.ExistsAsync(request.PropertyId, cancellationToken))
            return PropertyErrors.PropertyNotFound;

        return Result.Success(await rooms.ListGroupsAsync(request.PropertyId, cancellationToken));
    }
}
