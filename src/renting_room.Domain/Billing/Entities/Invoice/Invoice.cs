using renting_room.Domain.Common;
using renting_room.Domain.Contracts;

namespace renting_room.Domain.Billing;

/// <summary>
/// Phiếu báo tiền phòng (M07) — không phải hóa đơn GTGT (LEG-08). Nháp sửa được (sửa tay, phụ thu, tính lại);
/// chốt xong bất biến (BL-BR-14), chỉ hủy được khi chưa thu tiền (BL-BR-15).
/// </summary>
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

    /// <summary>Ngày 1 của tháng thu (tháng của <see cref="PeriodStart"/> — C-05).</summary>
    public DateOnly BillingMonth { get; private set; }
    public InvoiceStatus Status { get; private set; }
    public string? InvoiceNo { get; private set; }
    public DateOnly? IssueDate { get; private set; }
    public DateOnly? DueDate { get; private set; }
    public decimal Subtotal { get; private set; }
    public decimal DiscountTotal { get; private set; }

    /// <summary>Σ dòng hoàn trả (≤ 0) — tách khỏi giảm trừ vì được làm tổng phiếu âm (BL-BR-27).</summary>
    public decimal RefundTotal { get; private set; }

    /// <summary>
    /// BL-BR-29 (H1): "Làm tròn" — bỏ phần lẻ dưới 1.000đ của tổng (về 0: 523.560 ⇒ −560; −12.400 ⇒ +400). Không phải dòng phiếu
    /// (không bị tính lại / sửa tay riêng) — hiển thị thành dòng "Làm tròn" cuối phiếu.
    /// </summary>
    public decimal RoundingAmount { get; private set; }

    /// <summary>Khu bật làm tròn lúc tính phiếu gần nhất — giữ để sửa tay / thêm dòng sau đó vẫn làm tròn.</summary>
    public bool RoundTotal { get; private set; }

    /// <summary>BL-BR-20 (I1): "Cần tính lại" — dữ liệu nguồn đổi sau khi tính nháp; tính lại thì hết. Chỉ gợi ý, chốt vẫn kiểm (BL-BR-12).</summary>
    public bool IsStale { get; private set; }

    /// <summary>Subtotal + DiscountTotal + RefundTotal + RoundingAmount; âm ⇒ chủ trọ phải trả lại người thuê.</summary>
    public decimal TotalAmount { get; private set; }

    /// <summary>Tổng đã thu — M08 cập nhật trong cùng transaction với phân bổ (PM-BR-05).</summary>
    public decimal PaidAmount { get; private set; }

    /// <summary>Phần đã đóng bằng "bỏ nợ" (nằm trong <see cref="PaidAmount"/>) — loại khỏi doanh thu (PM-BR-16).</summary>
    public decimal WrittenOffAmount { get; private set; }
    public List<InvoiceIssue> Issues { get; private set; } = [];
    public string SnapshotRoomCode { get; private set; } = null!;
    public string SnapshotContractNo { get; private set; } = null!;
    public string SnapshotRepresentativeName { get; private set; } = null!;
    public string? Note { get; private set; }
    public DateTimeOffset? FinalizedAt { get; private set; }
    public DateTimeOffset? VoidedAt { get; private set; }
    public string? VoidReason { get; private set; }

    /// <summary>Xác nhận đã trả lại người thuê phần tổng âm (BL-BR-27).</summary>
    public DateOnly? RefundedOn { get; private set; }
    public PaymentMethod? RefundMethod { get; private set; }
    public string? RefundNote { get; private set; }

    public IReadOnlyList<InvoiceLine> Lines => _lines;
    public IReadOnlyList<InvoiceMeterSegment> Segments => _segments;

    /// <summary>Còn nợ (≥ 0) — phiếu tổng âm không bù trừ nợ của phiếu khác.</summary>
    public decimal Outstanding => Status == InvoiceStatus.Finalized ? Math.Max(TotalAmount - PaidAmount, 0) : 0;

    /// <summary>PM-BR-36: thực thu = tiền người thuê trả thật (<see cref="PaidAmount"/> gồm cả phần bỏ nợ).</summary>
    public decimal CollectedAmount => PaidAmount - WrittenOffAmount;

    /// <summary>Số chủ trọ còn phải trả lại người thuê (phiếu đã chốt, tổng âm, chưa xác nhận đã hoàn).</summary>
    public decimal RefundDue => Status == InvoiceStatus.Finalized && TotalAmount < 0 && RefundedOn is null ? -TotalAmount : 0;

    /// <summary>Phần thu sau giảm trừ (không tính hoàn trả) — luôn phải ≥ 0 (BL-BR-10).</summary>
    public decimal NetCharges => Subtotal + DiscountTotal;
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
            // C-05: tháng thu = tháng của kỳ chuẩn của khu chứa kỳ (kỳ đầu lẻ 03/11–04/11 của khu chốt ngày 5 ⇒ tháng 10).
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
        // BL-BR-33: dòng khoản phát sinh đã thanh toán / đã hoàn ngay hiện trên phiếu nhưng không tính vào tổng.
        Subtotal = _lines.Where(l => l.Amount > 0 && !l.IsSettled).Sum(l => l.Amount);
        DiscountTotal = _lines.Where(l => l.Amount < 0 && l.Type != InvoiceLineType.Refund && !l.IsSettled).Sum(l => l.Amount);
        RefundTotal = _lines.Where(l => l.Type == InvoiceLineType.Refund).Sum(l => l.Amount);
        var total = Subtotal + DiscountTotal + RefundTotal;
        RoundingAmount = RoundTotal ? -(total % RoundingUnit) : 0;
        TotalAmount = total + RoundingAmount;
    }

    /// <summary>BL-BR-29: làm tròn tổng phiếu tới nghìn đồng.</summary>
    public const decimal RoundingUnit = 1000;

    /// <summary>C-03: làm tròn ở cấp dòng, về đồng.</summary>
    public static decimal Money(decimal value) => decimal.Round(value, 0, MidpointRounding.AwayFromZero);
}
