using renting_room.Domain.Common;

namespace renting_room.Domain.Billing;

// Dòng của khoản phát sinh trên nháp (BL-BR-31..35)
public sealed partial class Invoice
{
    public InvoiceLine? ChargeLine(Guid roomChargeId) => _lines.FirstOrDefault(l => l.RoomChargeId == roomChargeId);

    /// <summary>BL-BR-31: đánh dấu / bỏ đánh dấu "đã thanh toán" của khoản phát sinh đang trên nháp ⇒ dòng ra / vào tổng.</summary>
    public Result SetChargeSettled(Guid roomChargeId, bool settled)
    {
        if (Status != InvoiceStatus.Draft)
            return Result.Failure(RoomChargeErrors.Locked);
        var line = ChargeLine(roomChargeId);
        if (line is null)
            return Result.Failure(BillingErrors.LineNotFound);
        if (line.IsSettled == settled)
            return Result.Success();
        // Bỏ đánh dấu một khoản bù / đánh dấu một khoản phụ thu đều có thể làm phần thu âm (BL-BR-10).
        if (NetCharges + (settled ? -line.Amount : line.Amount) < 0)
            return Result.Failure(BillingErrors.NegativeTotal);
        line.LinkCharge(roomChargeId, settled);
        Recompute();
        return Result.Success();
    }

    /// <summary>Hủy khoản phát sinh đang trên nháp ⇒ xóa dòng (BL-BR-35).</summary>
    public Result RemoveChargeLine(Guid roomChargeId)
    {
        if (Status != InvoiceStatus.Draft)
            return Result.Failure(RoomChargeErrors.Locked);
        return ChargeLine(roomChargeId) is { } line ? RemoveManualLine(line.Id) : Result.Success();
    }
}
