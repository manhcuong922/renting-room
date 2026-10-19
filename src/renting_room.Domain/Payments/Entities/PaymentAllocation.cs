using renting_room.Domain.Common;

namespace renting_room.Domain.Payments;

public sealed class PaymentAllocation : TenantEntity
{
    private PaymentAllocation() { } // EF Core

    internal PaymentAllocation(Guid paymentId, Guid contractId, Guid invoiceId, decimal amount)
    {
        Id = Guid.CreateVersion7();
        PaymentId = paymentId;
        ContractId = contractId;
        InvoiceId = invoiceId;
        Amount = amount;
    }

    public Guid PaymentId { get; private set; }
    public Guid ContractId { get; private set; }
    public Guid InvoiceId { get; private set; }
    public decimal Amount { get; private set; }
    public DateTimeOffset? CancelledAt { get; private set; }

    internal void Cancel(DateTimeOffset now) => CancelledAt ??= now;
}
