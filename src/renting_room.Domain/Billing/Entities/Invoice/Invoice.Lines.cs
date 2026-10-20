using renting_room.Domain.Common;

namespace renting_room.Domain.Billing;

public sealed partial class Invoice
{
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
        if (RejectsNegative(NetCharges - line.Amount + newAmount))
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
        if (type != InvoiceLineType.Refund && !isSettled && RejectsNegative(NetCharges + signed))
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
        if (line.Type != InvoiceLineType.Refund && !line.IsSettled && RejectsNegative(NetCharges - line.Amount + signed))
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
        if (line.Type != InvoiceLineType.Refund && !line.IsSettled && RejectsNegative(NetCharges - line.Amount))
            return Result.Failure(BillingErrors.NegativeTotal);

        _lines.Remove(line);
        Recompute();
        return Result.Success();
    }
}
