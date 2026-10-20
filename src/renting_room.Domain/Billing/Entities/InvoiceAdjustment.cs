using renting_room.Domain.Common;

namespace renting_room.Domain.Billing;

public enum AdjustmentHandling
{
    Recalculated,
    Immediate,
    NextInvoice
}

/// <summary>Một lần điều chỉnh chỉ số đã chốt (M07 BL-BR-40) — gắn với hóa đơn nguồn.</summary>
public sealed class InvoiceAdjustment : TenantEntity
{
    private InvoiceAdjustment() { } // EF Core

    public Guid ContractId { get; private set; }
    public Guid InvoiceId { get; private set; }
    public Guid MeterReadingId { get; private set; }
    public Guid FeeTypeId { get; private set; }
    public DateOnly UsageStart { get; private set; }
    public decimal OldValue { get; private set; }
    public decimal NewValue { get; private set; }
    public decimal OldQuantity { get; private set; }
    public decimal NewQuantity { get; private set; }
    public decimal OldAmount { get; private set; }
    public decimal NewAmount { get; private set; }
    public decimal Difference { get; private set; }
    public AdjustmentHandling Handling { get; private set; }
    public Guid? RoomChargeId { get; private set; }
    public List<Guid> PaymentIds { get; private set; } = [];
    public string Reason { get; private set; } = null!;

    public static InvoiceAdjustment Record(
        Invoice invoice, Guid readingId, MeteredLineChange change, decimal oldValue, decimal newValue, AdjustmentHandling handling,
        Guid? roomChargeId, IReadOnlyList<Guid> paymentIds, string reason) => new()
    {
        Id = Guid.CreateVersion7(),
        ContractId = invoice.ContractId,
        InvoiceId = invoice.Id,
        MeterReadingId = readingId,
        FeeTypeId = change.Line.FeeTypeId!.Value,
        UsageStart = change.Line.ServiceFrom,
        OldValue = oldValue,
        NewValue = newValue,
        OldQuantity = change.OldQuantity,
        NewQuantity = change.NewQuantity,
        OldAmount = change.OldAmount,
        NewAmount = change.NewAmount,
        Difference = change.Difference,
        Handling = handling,
        RoomChargeId = roomChargeId,
        PaymentIds = paymentIds.ToList(),
        Reason = reason.Trim()
    };
}
