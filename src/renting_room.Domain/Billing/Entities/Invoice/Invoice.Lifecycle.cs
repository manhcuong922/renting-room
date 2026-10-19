using renting_room.Domain.Common;

namespace renting_room.Domain.Billing;

// Chốt / hủy phiếu, ghi chú, ẩn danh (BL-BR-11..15)
public sealed partial class Invoice
{
    /// <summary>BL-BR-11/13: chốt — hết vấn đề chặn; cấp số, ngày phát hành, hạn thanh toán.</summary>
    public Result Finalize(string invoiceNo, DateOnly today, int paymentDueDays, DateTimeOffset now)
    {
        if (Status != InvoiceStatus.Draft)
            return Result.Failure(BillingErrors.NotDraft);
        if (HasBlockingIssues)
            return Result.Failure(BillingErrors.HasIssues(Issues.Where(i => i.Severity == InvoiceIssue.Error).Select(i => i.Code).Distinct().ToList()));
        if (NetCharges < 0)
            return Result.Failure(BillingErrors.NegativeTotal);

        InvoiceNo = invoiceNo;
        IssueDate = today;
        DueDate = today.AddDays(paymentDueDays);
        FinalizedAt = now;
        Status = InvoiceStatus.Finalized;
        return Result.Success();
    }

    /// <summary>BL-BR-15: hủy phiếu đã chốt chưa thu tiền — giải phóng đoạn đo để lập lại.</summary>
    public Result Void(string reason, DateTimeOffset now)
    {
        if (Status != InvoiceStatus.Finalized)
            return Result.Failure(BillingErrors.NotFinalized);
        if (PaidAmount > 0)
            return Result.Failure(BillingErrors.HasPayments);
        if (RefundedOn is not null)
            return Result.Failure(BillingErrors.RefundConfirmed);

        Status = InvoiceStatus.Void;
        VoidReason = reason.Trim();
        VoidedAt = now;
        foreach (var segment in _segments)
            segment.Void();
        return Result.Success();
    }

    public void UpdateNote(string? note) => Note = TextNormalizer.TrimToNull(note);

    /// <summary>BL-BR-14 / RT-BR-06: ngoại lệ duy nhất cho phiếu bất biến — lệnh ẩn danh thay tên người đứng tên đã chụp trên phiếu.</summary>
    public void AnonymizeRepresentative(string anonymizedName) => SnapshotRepresentativeName = anonymizedName;
}
