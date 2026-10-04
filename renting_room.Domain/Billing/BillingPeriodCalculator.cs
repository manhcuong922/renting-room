namespace renting_room.Domain.Billing;

public sealed record BillingPeriod(DateOnly Start, DateOnly End)
{
    /// <summary>Tháng thu = tháng của ngày bắt đầu kỳ (C-05), dạng "yyyy-MM" (VD "2026-10").</summary>
    public string BillingMonth => $"{Start.Year:D4}-{Start.Month:D2}";

    public bool Contains(DateOnly date) => date >= Start && date <= End;
}

/// <summary>
/// Hiện thực DUY NHẤT của kỳ thu (README C-05). Mọi module (hợp đồng, chỉ số, phiếu) dùng lớp này.
/// <list type="bullet">
/// <item><c>AnchorDate(y, m)</c> = ngày <c>anchorDay</c> của tháng, kẹp về cuối tháng nếu tháng ngắn hơn.</item>
/// <item>Kỳ đầu: từ ngày bắt đầu HĐ tới trước ngày chốt kế tiếp; nếu ngày chốt kế tiếp cùng tháng với ngày bắt đầu
/// thì gộp sang kỳ sau ⇒ mỗi tháng có tối đa 1 kỳ bắt đầu.</item>
/// <item>Các kỳ sau: [ngày chốt, ngày chốt kế tiếp − 1]. Kỳ cuối cắt tại ngày kết thúc thực tế.</item>
/// </list>
/// </summary>
public static class BillingPeriodCalculator
{
    public static DateOnly AnchorDate(int year, int month, int anchorDay)
    {
        EnsureAnchorDay(anchorDay);
        return new DateOnly(year, month, Math.Min(anchorDay, DateTime.DaysInMonth(year, month)));
    }

    /// <summary>Ngày chốt nhỏ nhất LỚN HƠN <paramref name="date"/>.</summary>
    public static DateOnly NextAnchor(DateOnly date, int anchorDay)
    {
        var sameMonth = AnchorDate(date.Year, date.Month, anchorDay);
        if (sameMonth > date)
            return sameMonth;

        var next = date.AddMonths(1);
        return AnchorDate(next.Year, next.Month, anchorDay);
    }

    /// <summary>Các kỳ của hợp đồng có ngày bắt đầu ≤ <paramref name="until"/>.</summary>
    public static IReadOnlyList<BillingPeriod> Periods(DateOnly contractStart, DateOnly? actualEnd, int anchorDay, DateOnly until)
    {
        EnsureAnchorDay(anchorDay);
        var periods = new List<BillingPeriod>();
        var start = contractStart;

        while (start <= until && (actualEnd is null || start <= actualEnd))
        {
            var end = PeriodEnd(start, contractStart, anchorDay);
            if (actualEnd is { } stop && end > stop)
                end = stop;

            periods.Add(new BillingPeriod(start, end));
            start = end.AddDays(1);
        }

        return periods;
    }

    /// <summary>Kỳ chứa ngày <paramref name="date"/> (null nếu ngày nằm ngoài thời gian hợp đồng).</summary>
    public static BillingPeriod? PeriodContaining(DateOnly contractStart, DateOnly? actualEnd, int anchorDay, DateOnly date)
    {
        if (date < contractStart || (actualEnd is { } stop && date > stop))
            return null;

        return Periods(contractStart, actualEnd, anchorDay, date).LastOrDefault(p => p.Contains(date));
    }

    /// <summary>
    /// C-05 prorate <c>Daily</c>: Σ (số ngày giao / độ dài kỳ chuẩn) qua các kỳ chuẩn giao với [<paramref name="start"/>, <paramref name="end"/>].
    /// Kỳ đầy đủ ⇒ 1; kỳ gộp 03/10–04/11 (chốt ngày 5) ⇒ 2/30 + 1. Không làm tròn — làm tròn một lần ở thành tiền.
    /// </summary>
    public static decimal ProrationFactor(DateOnly start, DateOnly end, int anchorDay)
    {
        EnsureAnchorDay(anchorDay);
        if (end < start)
            return 0;

        var standardStart = AnchorDate(start.Year, start.Month, anchorDay);
        if (standardStart > start)
        {
            var previous = start.AddMonths(-1);
            standardStart = AnchorDate(previous.Year, previous.Month, anchorDay);
        }

        decimal factor = 0;
        while (standardStart <= end)
        {
            var standardEnd = NextAnchor(standardStart, anchorDay).AddDays(-1);
            var from = standardStart > start ? standardStart : start;
            var to = standardEnd < end ? standardEnd : end;
            if (to >= from)
                factor += (decimal)(to.DayNumber - from.DayNumber + 1) / (standardEnd.DayNumber - standardStart.DayNumber + 1);
            standardStart = standardEnd.AddDays(1);
        }
        return factor;
    }

    public static bool IsPeriodStart(DateOnly contractStart, int anchorDay, DateOnly date) =>
        Periods(contractStart, actualEnd: null, anchorDay, date).Any(p => p.Start == date);

    private static DateOnly PeriodEnd(DateOnly periodStart, DateOnly contractStart, int anchorDay)
    {
        var next = NextAnchor(periodStart, anchorDay);

        // Kỳ đầu lẻ mà ngày chốt kế tiếp còn cùng tháng (VD bắt đầu 03/10, chốt ngày 5) ⇒ gộp vào kỳ sau.
        if (periodStart == contractStart && next.Year == periodStart.Year && next.Month == periodStart.Month)
            next = NextAnchor(next, anchorDay);

        return next.AddDays(-1);
    }

    private static void EnsureAnchorDay(int anchorDay)
    {
        if (anchorDay is < 1 or > 31)
            throw new ArgumentOutOfRangeException(nameof(anchorDay), "Anchor day must be between 1 and 31.");
    }
}
