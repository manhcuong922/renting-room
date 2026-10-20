using renting_room.Domain.Fees;

namespace renting_room.Domain.Billing;

public enum LinePriceSource
{
    Catalog,
    ContractOverride
}

public sealed record LinePriceTier(decimal From, decimal? UpTo, decimal UnitPrice, decimal Quantity, decimal Amount);

/// <summary>Bộ giá đã dùng để tính một dòng — đủ để tính lại thành tiền với sản lượng khác mà không đọc lại bảng giá.</summary>
public sealed record LinePrice(LinePriceSource Source, DateOnly? EffectiveFrom, decimal UnitPrice, IReadOnlyList<LinePriceTier>? Tiers)
{
    public static LinePrice Override(decimal unitPrice) => new(LinePriceSource.ContractOverride, null, unitPrice, null);

    public static LinePrice Of(FeePrice price, decimal quantity) =>
        new(LinePriceSource.Catalog, price.EffectiveFrom, price.UnitPrice, price.Tiers is { Count: > 0 } tiers ? Split(tiers, quantity) : null);

    public LinePrice WithQuantity(decimal quantity) =>
        Tiers is null ? this : this with { Tiers = Split(Tiers.Select(t => new PriceTier(t.UpTo, t.UnitPrice)).ToList(), quantity) };

    public decimal Amount(decimal quantity) =>
        Tiers is null ? Invoice.Money(quantity * UnitPrice) : Invoice.Money(WithQuantity(quantity).Tiers!.Sum(t => t.Quantity * t.UnitPrice));

    private static List<LinePriceTier> Split(IReadOnlyList<PriceTier> tiers, decimal quantity)
    {
        var result = new List<LinePriceTier>();
        decimal lower = 0;
        foreach (var tier in tiers)
        {
            var upper = tier.UpTo ?? decimal.MaxValue;
            var used = Math.Max(Math.Min(quantity, upper) - lower, 0);
            result.Add(new LinePriceTier(lower, tier.UpTo, tier.Price, used, Invoice.Money(used * tier.Price)));
            lower = upper;
        }
        return result;
    }
}
