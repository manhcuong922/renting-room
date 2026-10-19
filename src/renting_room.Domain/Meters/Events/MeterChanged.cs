using renting_room.Domain.Common;

namespace renting_room.Domain.Meters;

/// <summary>Công tơ được lắp / tháo / thay.</summary>
public sealed record MeterChanged(Guid MeterId, Guid RoomId) : IDomainEvent;
