using FluentValidation;
using renting_room.Application.Common.Interfaces;
using renting_room.Application.Common.Validation;
using renting_room.Domain.Common;
using renting_room.Domain.Fees;

namespace renting_room.Application.Fees;

internal static class FeeRules
{
    public const decimal MaxMeteredPrice = 100_000;
    public const decimal MaxOtherPrice = 50_000_000;

    public static decimal MaxPrice(FeeGroup group) => group == FeeGroup.Metered ? MaxMeteredPrice : MaxOtherPrice;

    public static bool IsValidPrice(decimal price, FeeGroup group) =>
        price >= 0 && price <= MaxPrice(group) && decimal.Round(price, 2) == price;

    public const int MaxTiers = 10;

    /// <summary>Bậc tăng dần theo <c>UpTo</c>, chỉ bậc cuối để trống (không giới hạn); giá mỗi bậc như giá theo công tơ.</summary>
    public static bool IsValidTiers(IReadOnlyList<PriceTier> tiers) =>
        tiers.Count is > 0 and <= MaxTiers
        && tiers.Take(tiers.Count - 1).All(t => t.UpTo is > 0)
        && tiers[^1].UpTo is null
        && tiers.Take(tiers.Count - 1).Zip(tiers.Skip(1).Take(tiers.Count - 2)).All(p => p.First.UpTo < p.Second.UpTo)
        && tiers.All(t => IsValidPrice(t.Price, FeeGroup.Metered));

    public static void PriceRules<T>(this AbstractValidator<T> v, Func<T, FeePriceInput?> price, Func<T, FeeGroup?> group, string path, TimeProvider clock)
    {
        v.RuleFor(x => price(x)!.EffectiveFrom)
            .Must(d => d <= clock.GetUtcNow().ToBusinessDate().AddDays(366)).When(x => price(x) is not null)
            .OverridePropertyName($"{path}effectiveFrom").WithErrorCode("OUT_OF_RANGE").WithMessage("Ngày hiệu lực tối đa sau hôm nay 1 năm.");
        v.RuleFor(x => price(x)!.UnitPrice)
            .NotNull().When(x => price(x) is { Tiers: null or { Count: 0 } })
            .OverridePropertyName($"{path}unitPrice").WithErrorCode("REQUIRED").WithMessage("Nhập đơn giá (hoặc giá theo bậc).");
        v.RuleFor(x => price(x)!.Tiers)
            .Must(t => t is null || t.Count == 0 || IsValidTiers(t)).When(x => price(x) is not null)
            .OverridePropertyName($"{path}tiers").WithErrorCode("INVALID_TIERS")
            .WithMessage($"Giá theo bậc: 1–{MaxTiers} bậc, mốc \"upTo\" tăng dần, bậc cuối để trống; giá mỗi bậc 0–100.000đ.");
        v.RuleFor(x => price(x)!.UnitPrice)
            .Must((x, p) => p is null || IsValidPrice(p.Value, group(x) ?? FeeGroup.Service)).When(x => price(x) is not null)
            .OverridePropertyName($"{path}unitPrice").WithErrorCode("INVALID_AMOUNT")
            .WithMessage("Đơn giá ≥ 0, tối đa 2 số lẻ; theo chỉ số ≤ 100.000đ/đơn vị, loại khác ≤ 50 triệu (chặn nhập thừa số 0).");
        v.RuleFor(x => price(x)!.Note).OptionalText(300).When(x => price(x) is not null).OverridePropertyName($"{path}note");
    }
}

internal static class FeeWarnings
{
    public static IReadOnlyList<Warning> ForPrice(FeeType fee, FeePrice price, IFeeSettings settings)
    {
        return fee.SystemCode == FeeSystemCodes.Electricity && price.HighestPrice > settings.ElectricityPriceWarningThreshold
            ? [new Warning("ELECTRICITY_PRICE_ABOVE_THRESHOLD",
                $"Giá điện {price.HighestPrice:N0}đ/kWh cao hơn mức tham chiếu {settings.ElectricityPriceWarningThreshold:N0}đ — " +
                "tiền điện thu của người thuê không được vượt giá bán lẻ (Thông tư 60/2025/TT-BCT).")]
            : [];
    }
}
