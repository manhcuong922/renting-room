namespace renting_room.Domain.Billing;

public sealed record CalculatedLine(
    InvoiceLineType Type,
    Guid? FeeTypeId,
    string Description,
    string? Unit,
    DateOnly ServiceFrom,
    DateOnly ServiceTo,
    decimal Quantity,
    decimal UnitPrice,
    decimal? ProrationFactor,
    decimal Amount,
    int SortOrder,
    LinePrice? Price = null)
{
    public (InvoiceLineType, Guid?, DateOnly) Key => (Type, FeeTypeId, ServiceFrom);
}
