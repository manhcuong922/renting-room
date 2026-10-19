using renting_room.Domain.Common;

namespace renting_room.Domain.Contracts;

/// <summary>Tài sản bàn giao kèm phòng (CT-UC-13) — căn cứ trừ cọc khi trả phòng.</summary>
public sealed class ContractAsset : TenantEntity
{
    private ContractAsset() { } // EF Core

    internal ContractAsset(Guid contractId, AssetInput input)
    {
        Id = Guid.CreateVersion7();
        ContractId = contractId;
        Update(input);
    }

    public Guid ContractId { get; private set; }
    public string Name { get; private set; } = null!;
    public int Quantity { get; private set; }
    public string? ConditionAtHandover { get; private set; }
    public string? ConditionAtReturn { get; private set; }
    public decimal? ValueEstimate { get; private set; }
    public string? Note { get; private set; }

    internal void Update(AssetInput input)
    {
        if (string.IsNullOrWhiteSpace(input.Name))
            throw new ArgumentException("Asset name is required.", nameof(input));
        if (input.Quantity is < 1 or > 100)
            throw new ArgumentOutOfRangeException(nameof(input), "Quantity must be between 1 and 100.");

        Name = input.Name.Trim();
        Quantity = input.Quantity;
        ConditionAtHandover = TextNormalizer.TrimToNull(input.ConditionAtHandover);
        ValueEstimate = input.ValueEstimate;
        Note = TextNormalizer.TrimToNull(input.Note);
    }

    /// <summary>CT-BR-23 (đổi 10/10/2026): chỉ ghi tình trạng lúc trả — tiền bồi thường thu bằng phụ thu trên phiếu (BL-BR-23).</summary>
    internal void RecordReturn(string? condition) => ConditionAtReturn = TextNormalizer.TrimToNull(condition);
}
