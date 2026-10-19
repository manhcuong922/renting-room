using FluentValidation;
using Mediator;
using renting_room.Application.Common.Interfaces;
using renting_room.Domain.Common;
using renting_room.Domain.Properties;

namespace renting_room.Application.Rooms;

public sealed record GetRoomQuery(Guid Id) : IRequest<Result<RoomDto>>;

public sealed class GetRoomHandler(IRoomQueries rooms, TimeProvider clock) : IRequestHandler<GetRoomQuery, Result<RoomDto>>
{
    public async ValueTask<Result<RoomDto>> Handle(GetRoomQuery request, CancellationToken cancellationToken)
    {
        var room = await rooms.GetAsync(request.Id, clock.GetUtcNow().ToBusinessDate(), cancellationToken);
        return room is null ? PropertyErrors.RoomNotFound : room;
    }
}
