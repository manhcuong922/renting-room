using renting_room.Domain.Common;
using renting_room.Domain.Contracts;

namespace renting_room.Domain.Billing;

// Trạng thái thu, ghi nhận tiền đã thu / bỏ nợ, hoàn tiền (PM-BR-04/05, BL-BR-27)
public sealed partial class Invoice
{
    public InvoicePaymentStatus? PaymentStatus(DateOnly today)
    {
        if (Status != InvoiceStatus.Finalized)
            return null;
        if (TotalAmount < 0)
            return RefundedOn is null ? InvoicePaymentStatus.RefundPending : InvoicePaymentStatus.Refunded;
        if (PaidAmount >= TotalAmount)
            return WrittenOffAmount > 0 ? InvoicePaymentStatus.WrittenOff : InvoicePaymentStatus.Paid;
        if (DueDate < today)
            return InvoicePaymentStatus.Overdue;
        // PM-BR-36: chỉ bỏ nợ một phần mà chưa thu đồng nào thì chưa phải "thu một phần".
        return CollectedAmount > 0 ? InvoicePaymentStatus.PartiallyPaid : InvoicePaymentStatus.Unpaid;
    }

    /// <summary>M08: cộng / trừ tiền đã thu (PM-BR-04/05) — giữ 0 ≤ đã thu ≤ tổng; <paramref name="writeOff"/> = bỏ nợ (PM-BR-16).</summary>
    public void ApplyPayment(decimal delta, bool writeOff = false)
    {
        if (Status != InvoiceStatus.Finalized)
            throw new InvalidOperationException("Payments apply to finalized invoices only.");
        var paid = PaidAmount + delta;
        if (paid < 0 || paid > TotalAmount)
            throw new InvalidOperationException("Paid amount out of range.");
        PaidAmount = paid;
        if (writeOff)
            WrittenOffAmount += delta;
    }

    /// <summary>BL-BR-27: xác nhận đã trả lại người thuê phần tổng âm của phiếu đã chốt.</summary>
    public Result ConfirmRefund(DateOnly refundedOn, PaymentMethod method, string? note, DateOnly today)
    {
        if (Status != InvoiceStatus.Finalized)
            return Result.Failure(BillingErrors.NotFinalized);
        if (TotalAmount >= 0)
            return Result.Failure(BillingErrors.NothingToRefund);
        if (RefundedOn is not null)
            return Result.Failure(BillingErrors.RefundConfirmed);
        if (refundedOn > today || refundedOn < IssueDate)
            return Result.Failure(BillingErrors.InvalidRefundDate);

        RefundedOn = refundedOn;
        RefundMethod = method;
        RefundNote = TextNormalizer.TrimToNull(note);
        return Result.Success();
    }

    /// <summary>Bỏ xác nhận đã hoàn (nhập nhầm) — cần trước khi hủy phiếu.</summary>
    public Result CancelRefund()
    {
        if (RefundedOn is null)
            return Result.Failure(BillingErrors.RefundNotConfirmed);
        RefundedOn = null;
        RefundMethod = null;
        RefundNote = null;
        return Result.Success();
    }
}
