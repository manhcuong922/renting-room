using renting_room.Domain.Common;

namespace renting_room.Domain.Meters;

public sealed record MeterChanged(Guid MeterId, Guid RoomId) : IDomainEvent;
