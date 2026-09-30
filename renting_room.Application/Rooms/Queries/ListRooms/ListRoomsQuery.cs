using Mediator;
using renting_room.Application.Rooms.Queries.GetRoom;

namespace renting_room.Application.Rooms.Queries.ListRooms;

public record ListRoomsQuery : IRequest<IReadOnlyList<RoomDto>>;
