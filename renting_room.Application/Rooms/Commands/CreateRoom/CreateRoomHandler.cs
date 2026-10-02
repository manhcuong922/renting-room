using Mediator;
using renting_room.Application.Common.Interfaces;
using renting_room.Domain.Common;
using renting_room.Domain.Entities;

namespace renting_room.Application.Rooms.Commands.CreateRoom;

public sealed class CreateRoomHandler(IAppDbContext db)
    : IRequestHandler<CreateRoomCommand, Result<Guid>>
{
    public async ValueTask<Result<Guid>> Handle(CreateRoomCommand request, CancellationToken cancellationToken)
    {
        var room = Room.Create(request.Name, request.MonthlyRent);

        db.Rooms.Add(room);
        await db.SaveChangesAsync(cancellationToken);

        return Result.Success(room.Id);
    }
}
