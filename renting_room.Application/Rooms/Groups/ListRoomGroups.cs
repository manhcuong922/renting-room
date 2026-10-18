using FluentValidation;
using Mediator;
using Microsoft.EntityFrameworkCore;
using renting_room.Application.Common.Interfaces;
using renting_room.Domain.Common;
using renting_room.Domain.Properties;

namespace renting_room.Application.Rooms;

public sealed record ListRoomGroupsQuery(Guid PropertyId) : IRequest<Result<IReadOnlyList<RoomGroupDto>>>;

public sealed class ListRoomGroupsHandler(IAppDbContext db) : IRequestHandler<ListRoomGroupsQuery, Result<IReadOnlyList<RoomGroupDto>>>
{
    public async ValueTask<Result<IReadOnlyList<RoomGroupDto>>> Handle(ListRoomGroupsQuery request, CancellationToken cancellationToken)
    {
        if (!await db.Properties.AnyAsync(p => p.Id == request.PropertyId, cancellationToken))
            return PropertyErrors.PropertyNotFound;

        var groups = await db.RoomGroups.AsNoTracking()
            .Where(g => g.PropertyId == request.PropertyId)
            .OrderBy(g => g.Name)
            .Select(g => new RoomGroupDto(g.Id, g.PropertyId, g.Name, g.Description, g.Members.Select(m => m.RoomId).ToList()))
            .ToListAsync(cancellationToken);
        return groups;
    }
}
