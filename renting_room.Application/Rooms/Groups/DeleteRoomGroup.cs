using Mediator;
using Microsoft.EntityFrameworkCore;
using renting_room.Application.Common.Interfaces;
using renting_room.Domain.Common;
using renting_room.Domain.Properties;

namespace renting_room.Application.Rooms;

public sealed record DeleteRoomGroupCommand(Guid Id) : IRequest<Result>;

/// <summary>PR-BR-08: khi có M07, chặn xóa nhóm đang được quy tắc điều chỉnh giá tham chiếu.</summary>
public sealed class DeleteRoomGroupHandler(IAppDbContext db) : IRequestHandler<DeleteRoomGroupCommand, Result>
{
    public async ValueTask<Result> Handle(DeleteRoomGroupCommand request, CancellationToken cancellationToken)
    {
        var group = await db.RoomGroups.Include(g => g.Members).FirstOrDefaultAsync(g => g.Id == request.Id, cancellationToken);
        if (group is null)
            return Result.Failure(PropertyErrors.RoomGroupNotFound);

        db.RoomGroups.Remove(group);
        await db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
