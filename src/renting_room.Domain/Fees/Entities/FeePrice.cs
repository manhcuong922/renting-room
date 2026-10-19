using renting_room.Domain.Common;

namespace renting_room.Domain.Fees;

/// <summary>Đơn giá có hiệu lực từ <see cref="EffectiveFrom"/> tới trước bản kế tiếp. Không sửa — xóa rồi thêm (FE-BR-07/08).</summary>
public sealed class FeePrice : TenantEntity
{
    private FeePrice() { } // EF Core

    internal FeePrice(Guid feeTypeId, DateOnly effectiveFrom, decimal unitPrice, string? note, IReadOnlyList<PriceTier>? tiers = null)
    {
        Id = Guid.CreateVersion7();
        FeeTypeId = feeTypeId;
        EffectiveFrom = effectiveFrom;
        Tiers = tiers is { Count: > 0 } ? tiers.ToList() : null;
        UnitPrice = Tiers?[0].Price ?? unitPrice; // theo bậc: đơn giá hiển thị = giá bậc 1
        Note = TextNormalizer.TrimToNull(note);
    }

    public Guid FeeTypeId { get; private set; }
    public DateOnly EffectiveFrom { get; private set; }
    public decimal UnitPrice { get; private set; }

    /// <summary>FE-BR-15: null = một giá; có = theo bậc (mốc lũy kế tăng dần, bậc cuối không giới hạn).</summary>
    public List<PriceTier>? Tiers { get; private set; }
    public string? Note { get; private set; }

    public bool IsTiered => Tiers is { Count: > 0 };

    /// <summary>Giá cao nhất (theo bậc ⇒ bậc cao nhất) — dùng cho cảnh báo vượt giá bán lẻ (FE-BR-13).</summary>
    public decimal HighestPrice => Tiers?.Max(t => t.Price) ?? UnitPrice;

    /// <summary>
    /// Thành tiền, làm tròn tới đồng (FE-BR-15). Một giá: số lượng × đơn giá. Theo bậc: mỗi phần sản lượng tính giá bậc của nó
    /// (VD 0–50 giá bậc 1, 51–100 giá bậc 2…) — bậc theo lượng tiêu thụ của kỳ, không quy đổi theo số ngày.
    /// <paramref name="unitPriceOverride"/> = giá riêng của HĐ (dịch vụ) — thay toàn bộ biểu giá.
    /// </summary>
    public decimal Amount(decimal quantity, decimal? unitPriceOverride = null)
    {
        if (quantity < 0)
            throw new ArgumentOutOfRangeException(nameof(quantity), "Quantity cannot be negative.");
        if (unitPriceOverride is { } overridePrice)
            return Round(quantity * overridePrice);
        if (Tiers is null)
            return Round(quantity * UnitPrice);

        decimal total = 0, lower = 0;
        foreach (var tier in Tiers)
        {
            if (quantity <= lower)
                break;
            var upper = tier.UpTo ?? decimal.MaxValue;
            total += (Math.Min(quantity, upper) - lower) * tier.Price;
            lower = upper;
        }
        return Round(total);
    }

    private static decimal Round(decimal value) => Math.Round(value, 0, MidpointRounding.AwayFromZero);
}
