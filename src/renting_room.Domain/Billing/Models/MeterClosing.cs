using renting_room.Domain.Contracts;

namespace renting_room.Domain.Billing;

public sealed record MeterClosing(Guid MeterId, Guid StartReadingId, Guid EndReadingId, Guid FeeTypeId, decimal Quantity, decimal UnitPrice);
