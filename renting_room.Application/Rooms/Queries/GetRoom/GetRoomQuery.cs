using Mediator;
using renting_room.Domain.Common;

namespace renting_room.Application.Rooms.Queries.GetRoom;

public record GetRoomQuery(Guid RoomId) : IRequest<Result<RoomDto>>;

public record RoomDto(Guid Id, string Name, decimal MonthlyRent, string Status, DateTimeOffset CreatedAt);
