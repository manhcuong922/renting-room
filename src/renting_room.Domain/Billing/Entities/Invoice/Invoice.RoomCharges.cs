using renting_room.Domain.Common;

namespace renting_room.Domain.Billing;

public sealed partial class Invoice
{
    public InvoiceLine? ChargeLine(Guid roomChargeId) => _lines.FirstOrDefault(l => l.RoomChargeId == roomChargeId);

    public Result SetChargeSettled(Guid roomChargeId, bool settled)
    {
        if (Status != InvoiceStatus.Draft)
            return Result.Failure(RoomChargeErrors.Locked);
        var line = ChargeLine(roomChargeId);
        if (line is null)
            return Result.Failure(BillingErrors.LineNotFound);
        if (line.IsSettled == settled)
            return Result.Success();
        if (RejectsNegative(NetCharges + (settled ? -line.Amount : line.Amount)))
            return Result.Failure(BillingErrors.NegativeTotal);
        line.LinkCharge(roomChargeId, settled);
        Recompute();
        return Result.Success();
    }

    public Result RemoveChargeLine(Guid roomChargeId)
    {
        if (Status != InvoiceStatus.Draft)
            return Result.Failure(RoomChargeErrors.Locked);
        return ChargeLine(roomChargeId) is { } line ? RemoveManualLine(line.Id) : Result.Success();
    }
}
