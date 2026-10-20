using renting_room.Domain.Common;

namespace renting_room.Domain.Billing;

public sealed class InvoiceLine : TenantEntity
{
    private InvoiceLine() { } // EF Core

    public Guid InvoiceId { get; private set; }
    public InvoiceLineType Type { get; private set; }

    public bool IsSystem { get; private set; }
    public Guid? FeeTypeId { get; private set; }
    public string Description { get; private set; } = null!;
    public string? Unit { get; private set; }
    public DateOnly ServiceFrom { get; private set; }
    public DateOnly ServiceTo { get; private set; }
    public decimal Quantity { get; private set; }
    public decimal UnitPrice { get; private set; }
    public decimal? ProrationFactor { get; private set; }
    public decimal Amount { get; private set; }
    public bool IsManuallyEdited { get; private set; }
    public decimal? SystemQuantity { get; private set; }
    public decimal? SystemUnitPrice { get; private set; }
    public decimal? SystemAmount { get; private set; }
    public string? Note { get; private set; }
    public int SortOrder { get; private set; }

    public Guid? SourceInvoiceId { get; private set; }

    public Guid? RoomChargeId { get; private set; }

    public bool IsSettled { get; private set; }

    public LinePrice? Price { get; private set; }

    internal void LinkCharge(Guid? roomChargeId, bool isSettled)
    {
        RoomChargeId = roomChargeId;
        IsSettled = isSettled;
    }

    public (InvoiceLineType, Guid?, DateOnly) Key => (Type, FeeTypeId, ServiceFrom);

    internal static InvoiceLine FromCalculation(Guid invoiceId, CalculatedLine line)
    {
        var result = new InvoiceLine { Id = Guid.CreateVersion7(), InvoiceId = invoiceId, IsSystem = true };
        result.Reset(line);
        return result;
    }

    internal static InvoiceLine Manual(
        Guid invoiceId, InvoiceLineType type, string description, decimal? quantity, decimal? unitPrice, decimal amount,
        string note, Guid? feeTypeId, DateOnly from, DateOnly to, int sortOrder, Guid? sourceInvoiceId) => new()
    {
        SourceInvoiceId = sourceInvoiceId,
        Id = Guid.CreateVersion7(),
        InvoiceId = invoiceId,
        Type = type,
        IsSystem = false,
        FeeTypeId = feeTypeId,
        Description = description.Trim(),
        ServiceFrom = from,
        ServiceTo = to,
        Quantity = quantity ?? 1,
        UnitPrice = unitPrice ?? Math.Abs(amount),
        Amount = amount,
        Note = note.Trim(),
        SortOrder = sortOrder
    };

    internal void Reset(CalculatedLine line)
    {
        Type = line.Type;
        FeeTypeId = line.FeeTypeId;
        Description = line.Description;
        Unit = line.Unit;
        ServiceFrom = line.ServiceFrom;
        ServiceTo = line.ServiceTo;
        Quantity = line.Quantity;
        UnitPrice = line.UnitPrice;
        ProrationFactor = line.ProrationFactor;
        Amount = line.Amount;
        SortOrder = line.SortOrder;
        Price = line.Price;
        IsManuallyEdited = false;
        SystemQuantity = SystemUnitPrice = SystemAmount = null;
        Note = null;
    }

    internal void RefreshSystem(CalculatedLine line)
    {
        Description = line.Description;
        Unit = line.Unit;
        ServiceFrom = line.ServiceFrom;
        ServiceTo = line.ServiceTo;
        ProrationFactor = line.ProrationFactor;
        SortOrder = line.SortOrder;
        SystemQuantity = line.Quantity;
        SystemUnitPrice = line.UnitPrice;
        SystemAmount = line.Amount;
        Price = line.Price;
    }

    internal void Edit(decimal quantity, decimal unitPrice, decimal amount, string? note)
    {
        if (!IsManuallyEdited)
        {
            SystemQuantity = Quantity;
            SystemUnitPrice = UnitPrice;
            SystemAmount = Amount;
            IsManuallyEdited = true;
        }
        Quantity = quantity;
        UnitPrice = unitPrice;
        Amount = amount;
        Note = TextNormalizer.TrimToNull(note);
    }

    internal void ClearEdit()
    {
        if (!IsManuallyEdited)
            return;
        Quantity = SystemQuantity!.Value;
        UnitPrice = SystemUnitPrice!.Value;
        Amount = SystemAmount!.Value;
        IsManuallyEdited = false;
        SystemQuantity = SystemUnitPrice = SystemAmount = null;
        Note = null;
    }

    /// <summary>
    /// Điều chỉnh chỉ số trên hóa đơn đã chốt: dòng hệ thống thay số; dòng sửa tay giữ số sửa tay cộng phần chênh — âm thì đặt 0 và trả true
    /// để báo người dùng kiểm tra lại.
    /// </summary>
    internal bool ApplyAdjustment(decimal quantity, decimal amount, decimal difference, LinePrice price)
    {
        Price = price;
        var unitPrice = price.Tiers is null || quantity == 0 ? price.UnitPrice : decimal.Round(amount / quantity, 2);
        if (IsManuallyEdited)
        {
            SystemQuantity = quantity;
            SystemUnitPrice = unitPrice;
            SystemAmount = amount;
            var clamped = Amount + difference < 0;
            Amount = Math.Max(Amount + difference, 0);
            return clamped;
        }
        Quantity = quantity;
        UnitPrice = unitPrice;
        ProrationFactor = price.Tiers is null || quantity == 0 ? null : amount / (quantity * unitPrice);
        Amount = amount;
        return false;
    }

    internal void UpdateManual(string description, decimal? quantity, decimal? unitPrice, decimal amount, string? note)
    {
        Description = description.Trim();
        Quantity = quantity ?? 1;
        UnitPrice = unitPrice ?? Math.Abs(amount);
        Amount = amount;
        Note = TextNormalizer.TrimToNull(note);
    }
}
