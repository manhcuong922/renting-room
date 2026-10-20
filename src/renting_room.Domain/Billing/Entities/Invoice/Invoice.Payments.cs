using renting_room.Domain.Common;
using renting_room.Domain.Contracts;

namespace renting_room.Domain.Billing;

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
        return CollectedAmount > 0 ? InvoicePaymentStatus.PartiallyPaid : InvoicePaymentStatus.Unpaid;
    }

    public void ApplyPayment(decimal delta, bool writeOff = false)
    {
        if (Status != InvoiceStatus.Finalized)
            throw new InvalidOperationException("Payments apply to finalized invoices only.");
        var paid = PaidAmount + delta;
        // Giảm được cả khi đang vượt tổng (hóa đơn vừa tính lại thấp hơn số đã thu — trả bớt / hoàn); tăng thì không vượt tổng.
        if (paid < 0 || (delta > 0 && paid > TotalAmount))
            throw new InvalidOperationException("Paid amount out of range.");
        PaidAmount = paid;
        if (writeOff)
            WrittenOffAmount += delta;
    }

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
