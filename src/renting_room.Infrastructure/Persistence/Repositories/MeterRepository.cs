using Microsoft.EntityFrameworkCore;
using renting_room.Application.Meters;
using renting_room.Domain.Meters;

namespace renting_room.Infrastructure.Persistence.Repositories;

internal sealed class MeterRepository(AppDbContext db) : IMeterRepository
{
    public async Task<List<Meter>> LockAndLoadAsync(IEnumerable<Guid> meterIds, CancellationToken cancellationToken)
    {
        var ids = meterIds.Distinct().OrderBy(id => id).ToList();
        foreach (var id in ids)
            await db.LockForUpdateAsync<Meter>(id, cancellationToken);
        return await db.Meters.Include(m => m.Readings).Where(m => ids.Contains(m.Id)).ToListAsync(cancellationToken);
    }

    public void Add(Meter meter) => db.Meters.Add(meter);

    public void AddRange(IEnumerable<Meter> meters) => db.Meters.AddRange(meters);
}
