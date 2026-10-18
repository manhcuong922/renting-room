using System.Text.Json;
using System.Text.Json.Serialization;
using renting_room.Domain.Contracts;
using renting_room.Domain.Fees;

namespace renting_room.Application.Contracts;

/// <summary>
/// Một khoản thu theo thỏa thuận lúc ký (CT-BR-19 <c>utility_price_snapshot</c>): tên, cách tính, số lượng, đơn giá tại ngày bắt đầu HĐ.
/// Gồm khoản gắn với HĐ và điện / nước theo công tơ của khu (tính theo công tơ của phòng — FE-BR-17).
/// <see cref="UnitPrice"/> null = lúc ký khoản này chưa có giá (in theo bảng giá khu tại ngày bắt đầu).
/// </summary>
public sealed record UtilityPriceItem(
    Guid FeeTypeId,
    string Name,
    FeeGroup Group,
    ChargeBasis? ChargeBasis,
    string Unit,
    decimal Quantity,
    decimal? UnitPrice,
    bool IsOverride,
    IReadOnlyList<PriceTier>? Tiers = null);

public static class UtilityPriceSnapshotJson
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    /// <summary>
    /// Khoản thu áp tại ngày bắt đầu HĐ: điện / nước theo công tơ của phòng (giá khu — <paramref name="types"/> chỉ chứa khoản phòng có công tơ,
    /// xem <c>ContractFeeRules.LoadTypesAsync</c>) + dịch vụ gắn với HĐ (giá riêng ưu tiên hơn giá khu).
    /// </summary>
    public static IReadOnlyList<UtilityPriceItem> Capture(Contract contract, IReadOnlyDictionary<Guid, FeeType> types)
    {
        var metered = types.Values
            .Where(t => t.Group == FeeGroup.Metered && t.PropertyId == contract.PropertyId && !t.IsArchived)
            .Select(t => (t.SortOrder, Item: new UtilityPriceItem(t.Id, t.Name, t.Group, null, t.Unit, 1,
                t.ResolvePrice(contract.StartDate)?.UnitPrice, IsOverride: false, t.ResolvePrice(contract.StartDate)?.Tiers)));
        var attached = contract.Fees
            .Where(f => f.Covers(contract.StartDate) && types.TryGetValue(f.FeeTypeId, out var t) && t.Group != FeeGroup.Metered)
            .Select(f =>
            {
                var type = types[f.FeeTypeId];
                return (type.SortOrder, Item: new UtilityPriceItem(f.FeeTypeId, type.Name, type.Group, type.ChargeBasis, type.Unit, f.Quantity,
                    f.UnitPriceOverride ?? type.ResolvePrice(contract.StartDate)?.UnitPrice, f.UnitPriceOverride is not null));
            });
        return metered.Concat(attached).OrderBy(x => x.SortOrder).Select(x => x.Item).ToList();
    }

    public static string ToJson(IReadOnlyList<UtilityPriceItem> items) => JsonSerializer.Serialize(items, Json);

    public static IReadOnlyList<UtilityPriceItem>? FromJson(string? json) =>
        json is null ? null : JsonSerializer.Deserialize<List<UtilityPriceItem>>(json, Json);
}
