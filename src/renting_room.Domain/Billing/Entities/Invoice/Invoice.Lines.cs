using renting_room.Domain.Common;

namespace renting_room.Domain.Billing;

// Sửa dòng hệ thống, dòng tay: phụ thu / giảm trừ / hoàn trả (BL-BR-07, BL-BR-23, BL-BR-27)
public sealed partial class Invoice
{
    /// <summary>BL-BR-07: sửa tay dòng hệ thống. Thành tiền bỏ trống ⇒ = số lượng × đơn giá × hệ số prorate.</summary>
    public Result EditLine(Guid lineId, decimal? quantity, decimal? unitPrice, decimal? amount, string? note)
    {
        if (Status != InvoiceStatus.Draft)
            return Result.Failure(BillingErrors.NotDraft);
        var line = _lines.FirstOrDefault(l => l.Id == lineId);
        if (line is null)
            return Result.Failure(BillingErrors.LineNotFound);
        if (!line.IsSystem)
            return UpdateManualLine(lineId, line.Description, quantity, unitPrice, amount ?? Math.Abs(line.Amount), note);
        if (line.Type == InvoiceLineType.Rent && string.IsNullOrWhiteSpace(note))
            return Result.Failure(BillingErrors.NoteRequired);

        var q = quantity ?? line.Quantity;
        var p = unitPrice ?? line.UnitPrice;
        var newAmount = amount ?? Money(q * p * (line.ProrationFactor ?? 1));
        if (NetCharges - line.Amount + newAmount < 0)
            return Result.Failure(BillingErrors.NegativeTotal);

        line.Edit(q, p, newAmount, note);
        Recompute();
        return Result.Success();
    }

    public Result ResetLine(Guid lineId)
    {
        if (Status != InvoiceStatus.Draft)
            return Result.Failure(BillingErrors.NotDraft);
        var line = _lines.FirstOrDefault(l => l.Id == lineId);
        if (line is null)
            return Result.Failure(BillingErrors.LineNotFound);
        if (!line.IsSystem)
            return RemoveManualLine(lineId);

        line.ClearEdit();
        Recompute();
        return Result.Success();
    }

    /// <summary>
    /// BL-BR-23 / BL-BR-27: phụ thu, giảm tay, hoàn trả — lý do bắt buộc, nhập số dương; giảm trừ và hoàn trả lưu số âm.
    /// Giảm trừ không vượt phần thu; hoàn trả được làm tổng phiếu âm (chủ trọ trả lại người thuê).
    /// </summary>
    /// <param name="sourceInvoiceId">BL-BR-27 (E): phiếu đã chốt bị tính sai mà người thuê đã trả — bắt buộc với dòng hoàn trả; số tiền tối đa
    /// (đã thu thật − các lần hoàn trước) do Application kiểm vì cần dữ liệu phiếu khác.</param>
    /// <param name="roomChargeId">Khoản phát sinh của phòng mà dòng này thể hiện (BL-BR-33).</param>
    /// <param name="isSettled">Khoản đã thanh toán / đã hoàn ngay ⇒ dòng hiện trên phiếu nhưng không tính vào tổng.</param>
    public Result<InvoiceLine> AddManualLine(
        InvoiceLineType type, string description, decimal? quantity, decimal? unitPrice, decimal amount, string? note, Guid? feeTypeId,
        Guid? sourceInvoiceId = null, Guid? roomChargeId = null, bool isSettled = false)
    {
        if (Status != InvoiceStatus.Draft)
            return Result.Failure<InvoiceLine>(BillingErrors.NotDraft);
        if (!IsManualType(type))
            throw new ArgumentException("Manual lines are surcharges, discounts or refunds.", nameof(type));
        if (string.IsNullOrWhiteSpace(note))
            return Result.Failure<InvoiceLine>(BillingErrors.NoteRequired);
        if (type == InvoiceLineType.Refund && (sourceInvoiceId is null || sourceInvoiceId == Id))
            return Result.Failure<InvoiceLine>(BillingErrors.RefundSourceRequired);

        var signed = Signed(type, amount);
        if (type != InvoiceLineType.Refund && !isSettled && NetCharges + signed < 0)
            return Result.Failure<InvoiceLine>(BillingErrors.NegativeTotal);

        var line = InvoiceLine.Manual(Id, type, description, quantity, unitPrice, signed, note, feeTypeId,
            PeriodStart, PeriodEnd, 1000 + _lines.Count(l => !l.IsSystem), type == InvoiceLineType.Refund ? sourceInvoiceId : null);
        line.LinkCharge(roomChargeId, isSettled && type != InvoiceLineType.Refund);
        _lines.Add(line);
        Recompute();
        return line;
    }

    public Result UpdateManualLine(Guid lineId, string description, decimal? quantity, decimal? unitPrice, decimal amount, string? note)
    {
        if (Status != InvoiceStatus.Draft)
            return Result.Failure(BillingErrors.NotDraft);
        var line = _lines.FirstOrDefault(l => l.Id == lineId && !l.IsSystem);
        if (line is null)
            return Result.Failure(BillingErrors.LineNotFound);
        if (string.IsNullOrWhiteSpace(note))
            return Result.Failure(BillingErrors.NoteRequired);

        var signed = Signed(line.Type, amount);
        if (line.Type != InvoiceLineType.Refund && !line.IsSettled && NetCharges - line.Amount + signed < 0)
            return Result.Failure(BillingErrors.NegativeTotal);
        line.UpdateManual(description, quantity, unitPrice, signed, note);
        Recompute();
        return Result.Success();
    }

    public Result RemoveManualLine(Guid lineId)
    {
        if (Status != InvoiceStatus.Draft)
            return Result.Failure(BillingErrors.NotDraft);
        var line = _lines.FirstOrDefault(l => l.Id == lineId && !l.IsSystem);
        if (line is null)
            return Result.Failure(BillingErrors.LineNotFound);
        if (line.Type != InvoiceLineType.Refund && !line.IsSettled && NetCharges - line.Amount < 0)
            return Result.Failure(BillingErrors.NegativeTotal);

        _lines.Remove(line);
        Recompute();
        return Result.Success();
    }
}
