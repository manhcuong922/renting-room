using FluentValidation;
using Mediator;
using Microsoft.EntityFrameworkCore;
using renting_room.Application.Common.Interfaces;
using renting_room.Domain.Common;
using renting_room.Domain.Properties;

namespace renting_room.Application.Meters;

public sealed record ListRoomMetersQuery(Guid RoomId, bool IncludeRemoved) : IRequest<Result<IReadOnlyList<MeterDto>>>;

public sealed class ListRoomMetersHandler(IAppDbContext db) : IRequestHandler<ListRoomMetersQuery, Result<IReadOnlyList<MeterDto>>>
{
    public async ValueTask<Result<IReadOnlyList<MeterDto>>> Handle(ListRoomMetersQuery request, CancellationToken cancellationToken)
    {
        if (!await db.Rooms.AnyAsync(r => r.Id == request.RoomId, cancellationToken))
            return PropertyErrors.RoomNotFound;

        var meters = await db.Meters.AsNoTracking().Include(m => m.Readings)
            .Where(m => m.RoomId == request.RoomId && (request.IncludeRemoved || m.RemovedDate == null))
            .ToListAsync(cancellationToken);
        var feeIds = meters.Select(m => m.FeeTypeId).Distinct().ToList();
        var fees = await db.FeeTypes.AsNoTracking().Where(f => feeIds.Contains(f.Id)).ToDictionaryAsync(f => f.Id, cancellationToken);

        return meters
            .OrderBy(m => fees.GetValueOrDefault(m.FeeTypeId)?.SortOrder).ThenByDescending(m => m.InstalledDate)
            .Select(m => m.ToDto(fees.GetValueOrDefault(m.FeeTypeId)))
            .ToList();
    }
}
