using renting_room.Domain.Contracts;
using renting_room.Domain.Fees;

namespace renting_room.Application.Fees;

/// <param name="Tiers">FE-BR-15: giá theo bậc (chỉ điện nước theo công tơ); null ⇒ một giá (<c>UnitPrice</c>).</param>
public sealed record FeePriceInput(DateOnly EffectiveFrom, decimal? UnitPrice, string? Note, IReadOnlyList<PriceTier>? Tiers = null);

public sealed record FeePriceDto(Guid Id, DateOnly EffectiveFrom, decimal UnitPrice, string? Note, IReadOnlyList<PriceTier>? Tiers);

public sealed record FeeTypeDto(
    Guid Id,
    Guid PropertyId,
    string Name,
    FeeGroup Group,
    ChargeBasis? ChargeBasis,
    string Unit,
    string? SystemCode,
    bool AutoAttach,
    decimal? DefaultQuantity,
    int SortOrder,
    VehicleType? VehicleType,
    bool IsArchived,
    FeePriceDto? CurrentPrice,
    IReadOnlyList<FeePriceDto> Prices,
    string Version)
{
    public static FeeTypeDto From(FeeType f, DateOnly today) => new(
        f.Id, f.PropertyId, f.Name, f.Group, f.ChargeBasis, f.Unit, f.SystemCode, f.AutoAttach, f.DefaultQuantity, f.SortOrder,
        f.VehicleType, f.IsArchived, f.ResolvePrice(today) is { } p ? ToDto(p) : null,
        f.Prices.OrderByDescending(p => p.EffectiveFrom).Select(ToDto).ToList(), f.Version.ToString());

    private static FeePriceDto ToDto(FeePrice p) => new(p.Id, p.EffectiveFrom, p.UnitPrice, p.Note, p.Tiers);
}
