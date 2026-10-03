using System.Text.RegularExpressions;

namespace renting_room.Domain.Contracts;

/// <summary>Cảnh báo mềm — không chặn thao tác, UI hiển thị để người dùng kiểm tra lại (plan M05 §8).</summary>
public sealed record ContractWarning(string Code, string Message);

public static partial class ContractWarnings
{
    public const int DepositWarningMonths = 3;

    public static readonly ContractWarning DepositAboveThreeMonths = new("DEPOSIT_ABOVE_THREE_MONTHS",
        $"Tiền cọc lớn hơn {DepositWarningMonths} tháng tiền thuê — kiểm tra lại số tiền.");

    /// <summary>
    /// Biển số VN sau chuẩn hóa (bỏ khoảng trắng, dấu): 2 số mã tỉnh + 1–2 chữ cái sê-ri (+ 1 số) + 4–5 số.
    /// VD 29B112345 (xe máy 29-B1 123.45), 30A12345 (ô tô 30A-123.45). Biển cũ / nước ngoài / ngoại giao có thể không khớp ⇒ chỉ cảnh báo.
    /// </summary>
    [GeneratedRegex("^[0-9]{2}[A-Z]{1,2}[0-9]?[0-9]{4,5}$")]
    private static partial Regex VietnamPlate();

    public static bool IsUsualPlate(string normalizedPlate) => VietnamPlate().IsMatch(normalizedPlate);

    public static ContractWarning UnusualPlate(string plate) => new("PLATE_FORMAT_UNUSUAL",
        $"Biển số \"{plate}\" không giống định dạng biển số Việt Nam — kiểm tra lại (biển cũ / nước ngoài thì bỏ qua).");

    public static IReadOnlyList<ContractWarning> For(Contract contract)
    {
        var warnings = new List<ContractWarning>();
        var initialRent = contract.RentTerms.MinBy(t => t.EffectiveFrom)?.MonthlyRent;
        if (initialRent is { } rent && contract.DepositAmount > rent * DepositWarningMonths)
            warnings.Add(DepositAboveThreeMonths);

        warnings.AddRange(contract.Vehicles
            .Where(v => v.IsActive && v.PlateNumber is not null && !IsUsualPlate(v.PlateNumber))
            .Select(v => UnusualPlate(v.PlateNumber!)));
        return warnings;
    }
}
