using FluentValidation;
using Mediator;
using Microsoft.EntityFrameworkCore;
using renting_room.Application.Common.Interfaces;
using renting_room.Domain.Common;
using renting_room.Domain.Properties;

namespace renting_room.Application.Rooms;

public sealed record GetRoomQuery(Guid Id) : IRequest<Result<RoomDto>>;

public sealed class GetRoomHandler(IAppDbContext db, TimeProvider clock) : IRequestHandler<GetRoomQuery, Result<RoomDto>>
{
    public async ValueTask<Result<RoomDto>> Handle(GetRoomQuery request, CancellationToken cancellationToken)
    {
        var today = clock.GetUtcNow().ToBusinessDate();
        var row = await db.Rooms.AsNoTracking()
            .Where(r => r.Id == request.Id)
            .ToRows(db, today)
            .FirstOrDefaultAsync(cancellationToken);

        return row is null ? PropertyErrors.RoomNotFound : (await new[] { row }.ToDtosAsync(db, today, cancellationToken))[0];
    }
}
