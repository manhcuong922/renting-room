namespace renting_room.Domain.Billing;

public enum InvoicePaymentStatus
{
    Unpaid,
    PartiallyPaid,
    Paid,
    Overdue,

    WrittenOff,

    RefundPending,

    Refunded
}
