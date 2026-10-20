using renting_room.Domain.Common;

namespace renting_room.Domain.Fees;

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

    public List<PriceTier>? Tiers { get; private set; }
    public string? Note { get; private set; }

    public bool IsTiered => Tiers is { Count: > 0 };

    public decimal HighestPrice => Tiers?.Max(t => t.Price) ?? UnitPrice;

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
