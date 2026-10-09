using renting_room.Domain.Properties;

namespace renting_room.Domain.Billing;

/// <param name="Month">Tháng thu (ngày 1 của tháng) — tháng của kỳ chuẩn của khu chứa kỳ này (C-05).</param>
public sealed record BillingPeriod(DateOnly Start, DateOnly End, DateOnly Month)
{
    /// <summary>Tháng thu dạng "yyyy-MM" (VD "2026-10").</summary>
    public string BillingMonth => $"{Month.Year:D4}-{Month.Month:D2}";

    public int Days => End.DayNumber - Start.DayNumber + 1;

    public bool Contains(DateOnly date) => date >= Start && date <= End;
}

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

/// <summary>Một mốc của lịch kỳ thu: từ <paramref name="EffectiveFrom"/> dùng ngày chốt / cách thu này (mốc đầu = <see cref="DateOnly.MinValue"/>).</summary>
/// <param name="AdjustDays">Số ngày tiền phòng cộng (+) / trừ (−) ở kỳ chuyển tiếp bắt đầu tại <paramref name="EffectiveFrom"/> (BL-BR-28).</param>
public sealed record BillingScheduleEntry(DateOnly EffectiveFrom, int AnchorDay, ChargeMode ChargeMode, int AdjustDays = 0);

/// <summary>
/// Hiện thực DUY NHẤT của kỳ thu (README C-05, PR-BR-09, K4). Ngày chốt / thu trước–thu sau là của <b>khu</b>, mọi HĐ dùng chung.
/// <list type="bullet">
/// <item>Kỳ chuẩn tháng M = [ngày chốt tháng M, ngày chốt tháng M+1 − 1]; tháng thu của kỳ = M.</item>
/// <item>Đổi ngày chốt từ mốc T (đầu kỳ chuẩn cũ tháng M, kỳ đầu chưa lập phiếu của khu): kỳ chuyển tiếp [T, ngày chốt mới tháng M+1 − 1]
/// vẫn là tháng M ⇒ mỗi tháng đúng 1 kỳ; từ đó theo ngày chốt mới.</item>
/// <item>Kỳ của HĐ = kỳ chuẩn cắt theo [Tính tiền từ ngày, ngày trả phòng] — kỳ đầu lẻ <b>không gộp</b> vào kỳ sau.</item>
/// </list>
/// </summary>
public sealed class BillingSchedule
{
    public const int MinAnchorDay = 1;
    public const int MaxAnchorDay = 28;
    /// <summary>BL-BR-28: lệch ≤ 3 ngày ⇒ mặc định không tính thêm / không trừ.</summary>
    public const int NegligibleDeviationDays = 3;

    private readonly BillingScheduleEntry[] _entries;

    public BillingSchedule(IEnumerable<BillingScheduleEntry> entries)
    {
        _entries = entries.OrderBy(e => e.EffectiveFrom).ToArray();
        if (_entries.Length == 0 || _entries[0].EffectiveFrom != DateOnly.MinValue)
            throw new ArgumentException("Schedule must start with an entry effective from DateOnly.MinValue.", nameof(entries));
        if (_entries.Any(e => e.AnchorDay is < MinAnchorDay or > MaxAnchorDay))
            throw new ArgumentOutOfRangeException(nameof(entries), "Anchor day must be between 1 and 28.");
    }

    public static BillingSchedule Single(int anchorDay, ChargeMode chargeMode) => new([new(DateOnly.MinValue, anchorDay, chargeMode)]);

    public IReadOnlyList<BillingScheduleEntry> Entries => _entries;

    public BillingScheduleEntry Current => _entries[^1];

    public static DateOnly AnchorDate(int year, int month, int anchorDay) =>
        new(year, month, Math.Min(anchorDay, DateTime.DaysInMonth(year, month)));

    /// <summary>Kỳ chuẩn của khu chứa <paramref name="date"/>.</summary>
    public StandardPeriod StandardPeriodContaining(DateOnly date)
    {
        var index = Array.FindLastIndex(_entries, e => e.EffectiveFrom <= date);
        var entry = _entries[index];
        if (index > 0)
        {
            var transition = Transition(index);
            if (date <= transition.End)
                return transition;
        }

        var start = AnchorDate(date.Year, date.Month, entry.AnchorDay);
        if (start > date)
        {
            var previous = date.AddMonths(-1);
            start = AnchorDate(previous.Year, previous.Month, entry.AnchorDay);
        }
        var next = start.AddMonths(1);
        var end = AnchorDate(next.Year, next.Month, entry.AnchorDay).AddDays(-1);
        return new StandardPeriod(start, end, new DateOnly(start.Year, start.Month, 1), false, end.DayNumber - start.DayNumber + 1, 0);
    }

    /// <summary>Cách thu (trước / sau) của kỳ chuẩn chứa <paramref name="date"/>.</summary>
    public ChargeMode ChargeModeOn(DateOnly date) => _entries[Array.FindLastIndex(_entries, e => e.EffectiveFrom <= date)].ChargeMode;

    /// <summary>Kỳ của HĐ có ngày bắt đầu ≤ <paramref name="until"/>: kỳ chuẩn cắt theo [<paramref name="billingStart"/>, <paramref name="actualEnd"/>].</summary>
    public IReadOnlyList<BillingPeriod> ContractPeriods(DateOnly billingStart, DateOnly? actualEnd, DateOnly until)
    {
        var periods = new List<BillingPeriod>();
        var start = billingStart;
        while (start <= until && (actualEnd is null || start <= actualEnd))
        {
            var standard = StandardPeriodContaining(start);
            var end = actualEnd is { } stop && stop < standard.End ? stop : standard.End;
            periods.Add(new BillingPeriod(start, end, standard.Month));
            start = end.AddDays(1);
        }
        return periods;
    }

    /// <summary>
    /// Số tháng tiền phòng của một kỳ HĐ (C-05, BL-BR-03, BL-BR-28): <c>Daily</c> = số ngày / độ dài kỳ chuẩn × số tháng của kỳ chuẩn;
    /// <c>FullPeriod</c> = số tháng của kỳ chuẩn (kỳ lẻ vẫn trọn tháng). Không làm tròn — làm tròn một lần ở thành tiền.
    /// </summary>
    public decimal RentFactor(DateOnly start, DateOnly end, ProrationMode mode)
    {
        if (end < start)
            return 0;
        var standard = StandardPeriodContaining(start);
        if (mode == ProrationMode.FullPeriod)
            return standard.RentMonths;
        var to = end < standard.End ? end : standard.End;
        return (decimal)(to.DayNumber - start.DayNumber + 1) / standard.Days * standard.RentMonths;
    }

    /// <summary>
    /// K4 (PR-BR-09): lịch mới khi đổi ngày chốt / cách thu từ kỳ chuẩn bắt đầu tại <paramref name="from"/> (kỳ đầu chưa lập phiếu của khu).
    /// Mốc đổi trước đó chưa có phiếu (từ <paramref name="from"/> trở đi) bị ghi đè. Đổi về đúng cài đặt cũ ⇒ không có kỳ chuyển tiếp.
    /// </summary>
    /// <param name="adjustDays">Null ⇒ gợi ý theo BL-BR-28 (lệch ≤ 3 ngày ⇒ 0, còn lại đủ số ngày lệch).</param>
    public BillingSchedule ChangeFrom(DateOnly from, int anchorDay, ChargeMode chargeMode, int? adjustDays)
    {
        if (StandardPeriodContaining(from).Start != from)
            throw new ArgumentException("A billing change must start at a standard period start.", nameof(from));
        var kept = _entries.Where(e => e.EffectiveFrom < from).ToList();
        var previous = kept[^1];
        if (previous.AnchorDay == anchorDay && previous.ChargeMode == chargeMode)
            return new BillingSchedule(kept);

        var deviation = TransitionDeviation(from, previous.AnchorDay, anchorDay);
        var adjust = adjustDays ?? SuggestedAdjustDays(deviation);
        if (!IsAdjustAllowed(deviation, adjust))
            throw new ArgumentOutOfRangeException(nameof(adjustDays));
        return new BillingSchedule([.. kept, new BillingScheduleEntry(from, anchorDay, chargeMode, adjust)]);
    }

    /// <summary>Số ngày kỳ chuyển tiếp bắt đầu tại <paramref name="from"/> dư (+) / thiếu (−) so với kỳ theo ngày chốt cũ.</summary>
    public static int TransitionDeviation(DateOnly from, int previousAnchorDay, int anchorDay)
    {
        var next = from.AddMonths(1);
        return AnchorDate(next.Year, next.Month, anchorDay).DayNumber - AnchorDate(next.Year, next.Month, previousAnchorDay).DayNumber;
    }

    public static int SuggestedAdjustDays(int deviation) => Math.Abs(deviation) <= NegligibleDeviationDays ? 0 : deviation;

    /// <summary>Dư N ngày ⇒ cộng 0..N; thiếu N ngày ⇒ trừ 0..N.</summary>
    public static bool IsAdjustAllowed(int deviation, int adjustDays) =>
        deviation >= 0 ? adjustDays >= 0 && adjustDays <= deviation : adjustDays <= 0 && adjustDays >= deviation;

    private StandardPeriod Transition(int index)
    {
        var entry = _entries[index];
        var previous = _entries[index - 1];
        var from = entry.EffectiveFrom;
        var next = from.AddMonths(1);
        var end = AnchorDate(next.Year, next.Month, entry.AnchorDay).AddDays(-1);
        var baseDays = AnchorDate(next.Year, next.Month, previous.AnchorDay).DayNumber - from.DayNumber;
        return new StandardPeriod(from, end, new DateOnly(from.Year, from.Month, 1), entry.AnchorDay != previous.AnchorDay, baseDays,
            entry.AdjustDays);
    }
}
