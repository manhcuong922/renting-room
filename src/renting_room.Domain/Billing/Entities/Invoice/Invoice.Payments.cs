using renting_room.Domain.Common;
using renting_room.Domain.Contracts;

namespace renting_room.Domain.Billing;

public sealed partial class Invoice
{
    /// <param name="moveOutStatus">Trạng thái của hóa đơn trả phòng đã nhận nợ của hóa đơn này (<see cref="CarriedToInvoiceId"/>).</param>
    public InvoicePaymentStatus? PaymentStatus(DateOnly today, InvoicePaymentStatus? moveOutStatus = null)
    {
        if (Status != InvoiceStatus.Finalized)
            return null;
        if (TotalAmount < 0)
        {
            if (RefundedOn is null)
                return InvoicePaymentStatus.RefundPending;
            return RefundWaived ? InvoicePaymentStatus.RefundWaived : InvoicePaymentStatus.Refunded;
        }
        if (PaidAmount >= TotalAmount)
        {
            if (CarriedOverAmount > 0)
                return CarriedStatus(moveOutStatus);
            return WrittenOffAmount > 0 ? InvoicePaymentStatus.WrittenOff : InvoicePaymentStatus.Paid;
        }
        if (DueDate < today)
            return InvoicePaymentStatus.Overdue;
        return CollectedAmount > 0 ? InvoicePaymentStatus.PartiallyPaid : InvoicePaymentStatus.Unpaid;
    }

    /// <summary>BL-BR-41: hóa đơn đã chuyển nợ đi theo hóa đơn trả phòng — xong ⇒ Hoàn thành, bỏ nợ ⇒ Đã bỏ nợ, chưa xong ⇒ chờ thu ở đó.</summary>
    private static InvoicePaymentStatus CarriedStatus(InvoicePaymentStatus? moveOutStatus) => moveOutStatus switch
    {
        InvoicePaymentStatus.Paid or InvoicePaymentStatus.Refunded or InvoicePaymentStatus.RefundWaived => InvoicePaymentStatus.CarriedSettled,
        InvoicePaymentStatus.WrittenOff => InvoicePaymentStatus.CarriedWrittenOff,
        _ => InvoicePaymentStatus.CarriedOver
    };

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

    /// <summary>
    /// BL-BR-41: chốt hóa đơn trả phòng ⇒ phần còn nợ của hóa đơn này chuyển vào đó (không phải thực thu). Số chuyển phải đúng số còn nợ
    /// lúc chốt — lệch ⇒ nháp đã cũ, tính lại.
    /// </summary>
    public Result CarryOverTo(Guid finalInvoiceId, decimal amount)
    {
        if (Status != InvoiceStatus.Finalized)
            return Result.Failure(BillingErrors.NotFinalized);
        if (CarriedToInvoiceId is not null || amount <= 0 || amount != Outstanding)
            return Result.Failure(BillingErrors.DraftStale);

        PaidAmount += amount;
        CarriedOverAmount = amount;
        CarriedToInvoiceId = finalInvoiceId;
        return Result.Success();
    }

    /// <summary>Hủy hóa đơn trả phòng ⇒ nợ quay lại hóa đơn này như trước khi chuyển.</summary>
    public void UndoCarryOver()
    {
        if (CarriedToInvoiceId is null)
            return;
        PaidAmount -= CarriedOverAmount;
        CarriedOverAmount = 0;
        CarriedToInvoiceId = null;
    }

    public Result ConfirmRefund(DateOnly refundedOn, PaymentMethod method, string? note, DateOnly today) =>
        RecordRefundDecision(refundedOn, method, note, today);

    /// <summary>
    /// Chủ trọ quyết định không hoàn phần người thuê được nhận lại (vd người thuê bỏ đi, không liên lạc được): không có cách hoàn, bắt buộc lý do.
    /// </summary>
    public Result WaiveRefund(DateOnly decidedOn, string? reason, DateOnly today)
    {
        var note = TextNormalizer.TrimToNull(reason);
        return note is null ? Result.Failure(BillingErrors.RefundWaiveReasonRequired) : RecordRefundDecision(decidedOn, null, note, today);
    }

    private Result RecordRefundDecision(DateOnly decidedOn, PaymentMethod? method, string? note, DateOnly today)
    {
        if (Status != InvoiceStatus.Finalized)
            return Result.Failure(BillingErrors.NotFinalized);
        if (TotalAmount >= 0)
            return Result.Failure(BillingErrors.NothingToRefund);
        if (RefundedOn is not null)
            return Result.Failure(BillingErrors.RefundConfirmed);
        if (decidedOn > today || decidedOn < IssueDate)
            return Result.Failure(BillingErrors.InvalidRefundDate);

        RefundedOn = decidedOn;
        RefundMethod = method;
        RefundWaived = method is null;
        RefundNote = TextNormalizer.TrimToNull(note);
        return Result.Success();
    }

    public Result CancelRefund()
    {
        if (RefundedOn is null)
            return Result.Failure(BillingErrors.RefundNotConfirmed);
        RefundedOn = null;
        RefundMethod = null;
        RefundWaived = false;
        RefundNote = null;
        return Result.Success();
    }
}
