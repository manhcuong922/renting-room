using renting_room.Domain.Contracts;

namespace renting_room.Domain.Payments;

public enum PaymentKind
{
    Receipt,
    WriteOff,
    Refund
}
