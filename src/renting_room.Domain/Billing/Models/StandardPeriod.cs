namespace renting_room.Domain.Billing;

/// <summary>
/// Kỳ chuẩn của khu. Kỳ chuyển tiếp (K4 — đổi ngày chốt khi khu đã có phiếu) dài / ngắn hơn 1 tháng:
/// <paramref name="BaseDays"/> = độ dài kỳ theo ngày chốt cũ, <paramref name="AdjustDays"/> = số ngày tiền phòng chủ trọ chọn cộng (+) / trừ (−).
/// </summary>
public sealed record StandardPeriod(DateOnly Start, DateOnly End, DateOnly Month, bool IsTransition, int BaseDays, int AdjustDays)
{
    public int Days => End.DayNumber - Start.DayNumber + 1;

    /// <summary>Số tháng tiền phòng của cả kỳ: kỳ thường = 1; kỳ chuyển tiếp = 1 + số ngày điều chỉnh / độ dài kỳ cũ (BL-BR-28).</summary>
    public decimal RentMonths => 1m + (decimal)AdjustDays / BaseDays;

    /// <summary>Số ngày kỳ chuyển tiếp dư (+) / thiếu (−) so với kỳ theo ngày chốt cũ.</summary>
    public int DeviationDays => Days - BaseDays;
}
