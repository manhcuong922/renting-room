namespace renting_room.Domain.Billing;

public enum InvoiceLineType
{
    Rent,
    Metered,
    Service,
    Surcharge,
    ManualDiscount,

    Refund
}
