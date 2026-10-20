using renting_room.Domain.Common;

namespace renting_room.Domain.Contracts;

public sealed record ContractBillingChanged(Guid ContractId) : IDomainEvent;
