using renting_room.Domain.Common;

namespace renting_room.Domain.Billing;

/// <param name="Line">Dòng điện nước chứa đoạn đo kết thúc ở chỉ số được điều chỉnh.</param>
/// <param name="OldAmount">Thành tiền hệ thống của dòng trước điều chỉnh (dòng sửa tay: số hệ thống).</param>
/// <param name="ManualAmountClamped">Dòng sửa tay cộng phần chênh ra âm — đã đặt 0đ.</param>
public sealed record MeteredLineChange(
    InvoiceLine Line, decimal OldQuantity, decimal NewQuantity, decimal OldAmount, decimal NewAmount, bool ManualAmountClamped = false)
{
    public decimal Difference => NewAmount - OldAmount;
}

public sealed partial class Invoice
{
    public InvoiceMeterSegment? SegmentEndingAt(Guid readingId) => _segments.FirstOrDefault(s => !s.Voided && s.EndReadingId == readingId);

    public InvoiceLine? LineOf(InvoiceMeterSegment segment) =>
        _lines.FirstOrDefault(l => l.IsSystem && l.Type == InvoiceLineType.Metered && segment.BelongsTo(l));

    /// <summary>
    /// Tiền của dòng điện nước nếu chỉ số cuối của đoạn đo đổi thành <paramref name="newEndValue"/> — tính theo bộ giá đã lưu trên dòng
    /// (<paramref name="fallbackPrice"/> khi dòng lập trước khi có bộ giá).
    /// </summary>
    public Result<MeteredLineChange> PreviewReadingChange(Guid endReadingId, decimal newEndValue, LinePrice? fallbackPrice)
    {
        var segment = SegmentEndingAt(endReadingId);
        if (segment is null || LineOf(segment) is not { } line)
            return Result.Failure<MeteredLineChange>(BillingErrors.LineNotFound);
        var price = line.Price ?? fallbackPrice;
        if (price is null)
            return Result.Failure<MeteredLineChange>(BillingErrors.PriceMissing);

        var oldQuantity = line.SystemQuantity ?? line.Quantity;
        var oldAmount = line.SystemAmount ?? line.Amount;
        var newQuantity = oldQuantity + (newEndValue - segment.EndValue);
        if (newQuantity < 0)
            return Result.Failure<MeteredLineChange>(BillingErrors.LineNotFound);
        return new MeteredLineChange(line, oldQuantity, newQuantity, oldAmount, price.Amount(newQuantity));
    }

    /// <summary>
    /// M06 MT-BR-19: tính lại dòng điện nước của hóa đơn đã chốt theo chỉ số cuối mới — giữ số hóa đơn / ngày lập / hạn; dòng sửa tay giữ
    /// số sửa tay cộng phần chênh. Đoạn đo ghi chỉ số mới để hiển thị đúng.
    /// </summary>
    public Result<MeteredLineChange> ApplyReadingChange(Guid endReadingId, decimal newEndValue, LinePrice? fallbackPrice)
    {
        if (Status != InvoiceStatus.Finalized)
            return Result.Failure<MeteredLineChange>(BillingErrors.NotFinalized);
        var change = PreviewReadingChange(endReadingId, newEndValue, fallbackPrice);
        if (change.IsFailure)
            return change;

        var (line, _, newQuantity, _, newAmount, _) = change.Value!;
        var price = (line.Price ?? fallbackPrice)!.WithQuantity(newQuantity);
        var clamped = line.ApplyAdjustment(newQuantity, newAmount, change.Value!.Difference, price);
        SegmentEndingAt(endReadingId)!.ChangeEnd(newEndValue);
        Recompute();
        IsAdjusted = true;
        NeedsResend = true;
        return change.Value with { ManualAmountClamped = clamped };
    }

    public void MarkAdjusted() => IsAdjusted = true;

    public Result MarkResent()
    {
        if (Status != InvoiceStatus.Finalized)
            return Result.Failure(BillingErrors.NotFinalized);
        NeedsResend = false;
        return Result.Success();
    }
}
