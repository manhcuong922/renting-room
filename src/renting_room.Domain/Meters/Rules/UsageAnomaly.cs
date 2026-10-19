namespace renting_room.Domain.Meters;

/// <summary>
/// MT-BR-08 (ngưỡng chốt 09/10/2026) — cảnh báo điện nước bất thường, <b>không chặn</b>: (1) sản lượng gấp ≥ 3 lần trung bình 3 kỳ trước
/// VÀ tăng ≥ 50 đơn vị (tránh báo nhầm phòng dùng ít: 10 → 35 kWh); chưa đủ 3 kỳ lịch sử ⇒ không xét; (2) = 0 khi phòng có người ở.
/// </summary>
public static class UsageAnomaly
{
    public const string Code = "UNUSUAL_USAGE";
    public const int HistoryPeriods = 3;
    public const decimal SpikeRatio = 3m;
    public const decimal SpikeMinIncrease = 50m;

    /// <param name="recent">Sản lượng các kỳ trước, mới nhất trước.</param>
    /// <returns>Câu cảnh báo, hoặc null nếu bình thường.</returns>
    public static string? Check(string name, string? unit, decimal quantity, IReadOnlyList<decimal> recent, bool hasOccupants)
    {
        if (quantity == 0 && hasOccupants)
            return $"{name} kỳ này = 0 {unit} trong khi phòng có người ở — kiểm lại chỉ số (có thể do đi vắng).";
        if (recent.Count < HistoryPeriods)
            return null;

        var average = recent.Take(HistoryPeriods).Average();
        if (quantity < SpikeRatio * average || quantity - average < SpikeMinIncrease)
            return null;
        var ratio = average > 0 ? $"gấp {quantity / average:0.#} lần" : "tăng đột biến so với";
        return $"{name} kỳ này {quantity:0.##} {unit}, {ratio} trung bình 3 kỳ trước ({average:0.##} {unit}) — kiểm lại chỉ số.";
    }

    /// <summary>Trung bình 3 kỳ gần nhất (null khi chưa đủ lịch sử) — để lưới chỉ số cảnh báo ngay khi nhập.</summary>
    public static decimal? RecentAverage(IReadOnlyList<decimal> recent) =>
        recent.Count < HistoryPeriods ? null : decimal.Round(recent.Take(HistoryPeriods).Average(), 2);
}
