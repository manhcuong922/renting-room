using Microsoft.EntityFrameworkCore;
using renting_room.Application.Rooms;
using renting_room.Domain.Properties;

namespace renting_room.Infrastructure.Persistence.Repositories;

internal sealed class RoomGroupRepository(AppDbContext db) : IRoomGroupRepository
{
    public Task<RoomGroup?> GetAsync(Guid id, CancellationToken cancellationToken) =>
        db.RoomGroups.Include(g => g.Members).FirstOrDefaultAsync(g => g.Id == id, cancellationToken);

    public Task<bool> IsNameTakenAsync(Guid propertyId, string name, Guid? excludeId, CancellationToken cancellationToken) =>
        db.RoomGroups.AnyAsync(g => g.PropertyId == propertyId && g.Name.ToLower() == name.ToLower() && g.Id != excludeId, cancellationToken);

    public void Add(RoomGroup group) => db.RoomGroups.Add(group);

    public void Remove(RoomGroup group) => db.RoomGroups.Remove(group);
}
