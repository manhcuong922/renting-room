using Mediator;
using renting_room.Application.Common.Interfaces;
using renting_room.Domain.Common;
using renting_room.Domain.Properties;

namespace renting_room.Application.Rooms;

public sealed record DeleteRoomGroupCommand(Guid Id) : IRequest<Result>;

public sealed class DeleteRoomGroupHandler(IRoomGroupRepository groups, IUnitOfWork unitOfWork) : IRequestHandler<DeleteRoomGroupCommand, Result>
{
    public async ValueTask<Result> Handle(DeleteRoomGroupCommand request, CancellationToken cancellationToken)
    {
        var group = await groups.GetAsync(request.Id, cancellationToken);
        if (group is null)
            return Result.Failure(PropertyErrors.RoomGroupNotFound);

        groups.Remove(group);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
