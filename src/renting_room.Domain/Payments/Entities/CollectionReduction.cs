using renting_room.Domain.Common;

namespace renting_room.Domain.Payments;

/// <summary>
/// M08 PM-BR-38 (8): một lần <b>giảm số đã thu</b> của hóa đơn (sửa "Đã thu" xuống hoặc đảo phiếu thu) — trước → sau, lý do, người sửa (CreatedBy).
/// Chỉ ghi; nguồn cột "Sửa giảm" của Excel thu tiền (M10 E2) — không lấy từ nhật ký vì nhật ký có hạn lưu.
/// </summary>
public sealed class CollectionReduction : TenantEntity, IPropertyScoped
{
    private CollectionReduction() { } // EF Core

    public Guid PropertyId { get; private set; }
    public Guid ContractId { get; private set; }
    public Guid InvoiceId { get; private set; }
    public decimal AmountBefore { get; private set; }
    public decimal AmountAfter { get; private set; }
    public string Reason { get; private set; } = null!;

    public decimal Amount => AmountBefore - AmountAfter;

    public static CollectionReduction Record(Guid propertyId, Guid contractId, Guid invoiceId, decimal before, decimal after, string reason)
    {
        if (after < 0 || after >= before)
            throw new ArgumentOutOfRangeException(nameof(after), "A reduction must lower the collected amount.");
        var text = TextNormalizer.TrimToNull(reason) ?? throw new ArgumentException("Reason is required.", nameof(reason));
        return new CollectionReduction
        {
            Id = Guid.CreateVersion7(),
            PropertyId = propertyId,
            ContractId = contractId,
            InvoiceId = invoiceId,
            AmountBefore = before,
            AmountAfter = after,
            Reason = text.Length > 500 ? text[..500] : text
        };
    }
}
