using renting_room.Domain.Common;
using renting_room.Domain.Contracts;

namespace renting_room.Domain.Payments;

public enum PaymentStatus
{
    Recorded,
    Reversed
}

/// <summary><c>Receipt</c> = tiền thật; <c>WriteOff</c> = bỏ nợ (PM-BR-16) — đóng công nợ, không tính doanh thu.</summary>
public enum PaymentKind
{
    Receipt,
    WriteOff
}

/// <summary>
/// Phiếu thu (M08) — tiền thực nhận của 1 HĐ, phân bổ vào phiếu báo đã chốt. Không sửa số tiền: nhập sai thì đảo (PM-BR-01/07).
/// </summary>
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

    /// <summary>RT-BR-06: xóa tên người nộp khi ẩn danh người thuê — số tiền, ngày, phân bổ giữ nguyên.</summary>
    public void ClearPayerName() => PayerName = null;

    /// <param name="allocations">(phiếu báo, số tiền) — tổng phải bằng số tiền phiếu thu (đợt 1 chưa có số dư có).</param>
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

    /// <summary>PM-BR-07: đảo phiếu thu — hủy mọi phân bổ; trả (phiếu báo, số tiền) để giảm tiền đã thu.</summary>
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

public static class PaymentErrors
{
    public static readonly Error NotFound = Error.NotFound("PAYMENT_NOT_FOUND", "Không tìm thấy phiếu thu.");
    public static readonly Error AlreadyReversed = Error.Conflict("PAYMENT_ALREADY_REVERSED", "Phiếu thu đã đảo.");
    public static readonly Error ExceedsDebt = Error.BusinessRule("PAYMENT_EXCEEDS_DEBT",
        "Số tiền lớn hơn số còn nợ — nhập đúng số tiền còn nợ (chưa hỗ trợ trả thừa).");
    public static readonly Error ContractNotBillable = Error.BusinessRule("CONTRACT_NOT_BILLABLE",
        "Hợp đồng đã kết thúc — không ghi / đảo phiếu thu (hoàn tất thanh lý đã xử lý hết nợ).");
    public static readonly Error PaidBeforeContract = Error.Validation("INVALID_PAID_AT", "Ngày thu không được trước ngày bắt đầu hợp đồng quá 60 ngày.");
    public static readonly Error NoDebt = Error.BusinessRule("NO_OUTSTANDING_INVOICE", "Hợp đồng không còn phiếu nào chưa thu đủ.");
}
