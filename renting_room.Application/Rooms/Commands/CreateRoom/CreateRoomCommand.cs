using Mediator;
using renting_room.Domain.Common;

namespace renting_room.Application.Rooms.Commands.CreateRoom;

public record CreateRoomCommand(string Name, decimal MonthlyRent) : IRequest<Result<Guid>>;
