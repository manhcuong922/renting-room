using Mediator;
using Microsoft.EntityFrameworkCore;
using renting_room.Application.Common.Interfaces;
using renting_room.Application.Rooms.Queries.GetRoom;

namespace renting_room.Application.Rooms.Queries.ListRooms;

public sealed class ListRoomsHandler(IAppDbContext db) : IRequestHandler<ListRoomsQuery, IReadOnlyList<RoomDto>>
{
    public async ValueTask<IReadOnlyList<RoomDto>> Handle(ListRoomsQuery request, CancellationToken cancellationToken)
    {
        return await db.Rooms
            .OrderBy(r => r.Name)
            .Select(r => new RoomDto(r.Id, r.Name, r.MonthlyRent, r.Status.ToString(), r.CreatedAt))
            .ToListAsync(cancellationToken);
    }
}
