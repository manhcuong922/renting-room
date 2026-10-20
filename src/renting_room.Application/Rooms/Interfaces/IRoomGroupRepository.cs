using renting_room.Domain.Properties;

namespace renting_room.Application.Rooms;

public interface IRoomGroupRepository
{
    Task<RoomGroup?> GetAsync(Guid id, CancellationToken cancellationToken);

    Task<bool> IsNameTakenAsync(Guid propertyId, string name, Guid? excludeId, CancellationToken cancellationToken);

    void Add(RoomGroup group);

    void Remove(RoomGroup group);
}
