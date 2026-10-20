using renting_room.Domain.Common;

namespace renting_room.Domain.Fees;

public sealed record FeeTypeChanged(Guid FeeTypeId) : IDomainEvent;
