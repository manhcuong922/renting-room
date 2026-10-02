using Mediator;
using Microsoft.EntityFrameworkCore;
using renting_room.Application.Common.Interfaces;
using renting_room.Domain.Common;
using renting_room.Domain.Entities;

namespace renting_room.Application.Rooms.Queries.GetRoom;

public sealed class GetRoomHandler(IAppDbContext db) : IRequestHandler<GetRoomQuery, Result<RoomDto>>
{
    public async ValueTask<Result<RoomDto>> Handle(GetRoomQuery request, CancellationToken cancellationToken)
    {
        var room = await db.Rooms
            .Where(r => r.Id == request.RoomId)
            .Select(r => new RoomDto(r.Id, r.Name, r.MonthlyRent, r.Status.ToString(), r.CreatedAt))
            .FirstOrDefaultAsync(cancellationToken);

        return room is not null
            ? Result.Success(room)
            : Result.Failure<RoomDto>(RoomErrors.NotFound);
    }
}
