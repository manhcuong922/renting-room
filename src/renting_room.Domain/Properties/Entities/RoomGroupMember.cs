using renting_room.Domain.Common;

namespace renting_room.Domain.Properties;

public sealed class RoomGroupMember : TenantEntity
{
    private RoomGroupMember() { } // EF Core

    internal RoomGroupMember(Guid roomGroupId, Guid propertyId, Guid roomId)
    {
        Id = Guid.CreateVersion7();
        RoomGroupId = roomGroupId;
        PropertyId = propertyId;
        RoomId = roomId;
    }

    public Guid RoomGroupId { get; private set; }
    public Guid PropertyId { get; private set; }
    public Guid RoomId { get; private set; }
}
