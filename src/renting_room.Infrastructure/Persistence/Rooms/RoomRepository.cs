using Microsoft.EntityFrameworkCore;
using renting_room.Application.Rooms;
using renting_room.Domain.Properties;

namespace renting_room.Infrastructure.Persistence.Repositories;

internal sealed class RoomRepository(AppDbContext db) : IRoomRepository
{
    public Task<Room?> GetAsync(Guid id, CancellationToken cancellationToken) =>
        db.Rooms.FirstOrDefaultAsync(r => r.Id == id, cancellationToken);

    public async Task<Room?> GetForUpdateAsync(Guid id, CancellationToken cancellationToken)
    {
        await db.LockForUpdateAsync<Room>(id, cancellationToken);
        return await GetAsync(id, cancellationToken);
    }

    public async Task<IReadOnlyList<Room>> ListActiveByPropertyAsync(Guid propertyId, CancellationToken cancellationToken) =>
        await db.Rooms.Where(r => r.PropertyId == propertyId && r.ArchivedAt == null).ToListAsync(cancellationToken);

    public Task<bool> IsCodeTakenAsync(Guid propertyId, string code, Guid? excludeId, CancellationToken cancellationToken) =>
        db.Rooms.AnyAsync(r => r.PropertyId == propertyId && r.Code == code && (excludeId == null || r.Id != excludeId.Value), cancellationToken);

    public async Task<IReadOnlyList<string>> GetExistingCodesAsync(
        Guid propertyId, IReadOnlyCollection<string> codes, CancellationToken cancellationToken) =>
        await db.Rooms.Where(r => r.PropertyId == propertyId && codes.Contains(r.Code)).Select(r => r.Code).ToListAsync(cancellationToken);

    public void Add(Room room) => db.Rooms.Add(room);

    public void AddRange(IEnumerable<Room> rooms) => db.Rooms.AddRange(rooms);
}
