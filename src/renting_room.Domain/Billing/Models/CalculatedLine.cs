namespace renting_room.Domain.Billing;

/// <summary>Dòng hệ thống tính ra (chưa gắn vào phiếu).</summary>
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
    int SortOrder)
{
    /// <summary>Nhận diện dòng hệ thống giữa các lần tính — gồm ngày đầu khoảng dịch vụ vì phiếu quyết toán có thể có 2 dòng cùng khoản (BL-BR-05).</summary>
    public (InvoiceLineType, Guid?, DateOnly) Key => (Type, FeeTypeId, ServiceFrom);
}
