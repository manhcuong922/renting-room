using renting_room.Domain.Common;
using renting_room.Domain.Contracts;

namespace renting_room.Domain.Billing;

public sealed partial class Invoice : TenantEntity
{
    private readonly List<InvoiceLine> _lines = [];
    private readonly List<InvoiceMeterSegment> _segments = [];

    private Invoice() { } // EF Core

    public Guid PropertyId { get; private set; }
    public Guid RoomId { get; private set; }
    public Guid ContractId { get; private set; }
    public InvoiceType Type { get; private set; }
    public DateOnly PeriodStart { get; private set; }
    public DateOnly PeriodEnd { get; private set; }

    public DateOnly BillingMonth { get; private set; }
    public InvoiceStatus Status { get; private set; }
    public string? InvoiceNo { get; private set; }
    public DateOnly? IssueDate { get; private set; }
    public DateOnly? DueDate { get; private set; }
    public decimal Subtotal { get; private set; }
    public decimal DiscountTotal { get; private set; }

    public decimal RefundTotal { get; private set; }

    public decimal RoundingAmount { get; private set; }

    /// <summary>Khu bật làm tròn lúc tính phiếu gần nhất — giữ để sửa tay / thêm dòng sau đó vẫn làm tròn.</summary>
    public bool RoundTotal { get; private set; }

    public bool IsStale { get; private set; }

    public bool IsAdjusted { get; private set; }

    public bool NeedsResend { get; private set; }

    /// <summary>Subtotal + DiscountTotal + RefundTotal + RoundingAmount; âm ⇒ chủ trọ phải trả lại người thuê.</summary>
    public decimal TotalAmount { get; private set; }

    public decimal PaidAmount { get; private set; }

    public decimal WrittenOffAmount { get; private set; }
    public List<InvoiceIssue> Issues { get; private set; } = [];
    public string SnapshotRoomCode { get; private set; } = null!;
    public string SnapshotContractNo { get; private set; } = null!;
    public string SnapshotRepresentativeName { get; private set; } = null!;
    public string? Note { get; private set; }
    public DateTimeOffset? FinalizedAt { get; private set; }
    public DateTimeOffset? VoidedAt { get; private set; }
    public string? VoidReason { get; private set; }

    public DateOnly? RefundedOn { get; private set; }
    public PaymentMethod? RefundMethod { get; private set; }
    public string? RefundNote { get; private set; }

    public IReadOnlyList<InvoiceLine> Lines => _lines;
    public IReadOnlyList<InvoiceMeterSegment> Segments => _segments;

    public decimal Outstanding => Status == InvoiceStatus.Finalized ? Math.Max(TotalAmount - PaidAmount, 0) : 0;

    public decimal CollectedAmount => PaidAmount - WrittenOffAmount;

    public decimal RefundDue => Status == InvoiceStatus.Finalized && TotalAmount < 0 && RefundedOn is null ? -TotalAmount : 0;

    public decimal NetCharges => Subtotal + DiscountTotal;

    /// <summary>Hóa đơn trả phòng là hóa đơn cuối: khoản bù được làm phần thu âm (Chờ hoàn) thay vì treo chờ hóa đơn sau.</summary>
    private bool RejectsNegative(decimal netCharges) => Type != InvoiceType.Final && netCharges < 0;
    public bool HasBlockingIssues => Issues.Any(i => i.Severity == InvoiceIssue.Error);

    public static Invoice CreateDraft(
        Guid propertyId, Guid roomId, Guid contractId, BillingPeriod period,
        string roomCode, string contractNo, string representativeName, InvoiceCalculation calculation, InvoiceType type = InvoiceType.Regular)
    {
        var invoice = new Invoice
        {
            Id = Guid.CreateVersion7(),
            PropertyId = propertyId,
            RoomId = roomId,
            ContractId = contractId,
            Type = type,
            PeriodStart = period.Start,
            PeriodEnd = period.End,
            BillingMonth = period.Month,
            Status = InvoiceStatus.Draft,
            SnapshotRoomCode = roomCode,
            SnapshotContractNo = contractNo,
            SnapshotRepresentativeName = representativeName
        };
        invoice.ApplyCalculation(calculation, keepManualEdits: false);
        return invoice;
    }

    public static bool IsManualType(InvoiceLineType type) =>
        type is InvoiceLineType.Surcharge or InvoiceLineType.ManualDiscount or InvoiceLineType.Refund;

    private static decimal Signed(InvoiceLineType type, decimal amount) =>
        type is InvoiceLineType.ManualDiscount or InvoiceLineType.Refund ? -amount : amount;

    private void Recompute()
    {
        Subtotal = _lines.Where(l => l.Amount > 0 && !l.IsSettled).Sum(l => l.Amount);
        DiscountTotal = _lines.Where(l => l.Amount < 0 && l.Type != InvoiceLineType.Refund && !l.IsSettled).Sum(l => l.Amount);
        RefundTotal = _lines.Where(l => l.Type == InvoiceLineType.Refund).Sum(l => l.Amount);
        var total = Subtotal + DiscountTotal + RefundTotal;
        RoundingAmount = RoundTotal ? -(total % RoundingUnit) : 0;
        TotalAmount = total + RoundingAmount;
    }

    public const decimal RoundingUnit = 1000;

    public static decimal Money(decimal value) => decimal.Round(value, 0, MidpointRounding.AwayFromZero);
}
