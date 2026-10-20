using renting_room.Domain.Common;
using renting_room.Domain.Contracts;

namespace renting_room.Domain.Payments;

public sealed class Payment : TenantEntity
{
    public const decimal MaxAmount = 10_000_000_000;

    private readonly List<PaymentAllocation> _allocations = [];

    private Payment() { } // EF Core

    public Guid PropertyId { get; private set; }
    public Guid ContractId { get; private set; }
    public string ReceiptNo { get; private set; } = null!;
    public decimal Amount { get; private set; }
    public PaymentMethod Method { get; private set; }
    public DateOnly PaidAt { get; private set; }
    public string? PayerName { get; private set; }
    public string? Reference { get; private set; }
    public string? Note { get; private set; }
    public PaymentStatus Status { get; private set; }
    public PaymentKind Kind { get; private set; }
    public DateTimeOffset? ReversedAt { get; private set; }
    public string? ReverseReason { get; private set; }

    public IReadOnlyList<PaymentAllocation> Allocations => _allocations;

    public void ClearPayerName() => PayerName = null;

    public static Payment Record(
        Guid propertyId, Guid contractId, string receiptNo, decimal amount, PaymentMethod method, DateOnly paidAt,
        string? payerName, string? reference, string? note, IReadOnlyList<(Guid InvoiceId, decimal Amount)> allocations,
        PaymentKind kind = PaymentKind.Receipt)
    {
        if (kind == PaymentKind.WriteOff && string.IsNullOrWhiteSpace(note))
            throw new ArgumentException("Write-off reason is required.", nameof(note));
        if (amount <= 0 || amount > MaxAmount || decimal.Round(amount, 0) != amount)
            throw new ArgumentOutOfRangeException(nameof(amount));
        if (allocations.Count == 0 || allocations.Any(a => a.Amount <= 0) || allocations.Sum(a => a.Amount) != amount)
            throw new ArgumentException("Allocations must cover the payment exactly.", nameof(allocations));

        var payment = new Payment
        {
            Id = Guid.CreateVersion7(),
            PropertyId = propertyId,
            ContractId = contractId,
            ReceiptNo = receiptNo,
            Amount = amount,
            Method = method,
            PaidAt = paidAt,
            PayerName = TextNormalizer.TrimToNull(payerName),
            Reference = TextNormalizer.TrimToNull(reference),
            Note = TextNormalizer.TrimToNull(note),
            Status = PaymentStatus.Recorded,
            Kind = kind
        };
        payment._allocations.AddRange(allocations.Select(a => new PaymentAllocation(payment.Id, contractId, a.InvoiceId, a.Amount)));
        return payment;
    }

    public Result<IReadOnlyList<(Guid InvoiceId, decimal Amount)>> Reverse(string reason, DateTimeOffset now)
    {
        if (Status == PaymentStatus.Reversed)
            return Result.Failure<IReadOnlyList<(Guid, decimal)>>(PaymentErrors.AlreadyReversed);

        Status = PaymentStatus.Reversed;
        ReversedAt = now;
        ReverseReason = reason.Trim();
        var released = _allocations.Where(a => a.CancelledAt is null).Select(a => (a.InvoiceId, a.Amount)).ToList();
        foreach (var allocation in _allocations)
            allocation.Cancel(now);
        return released;
    }
}
