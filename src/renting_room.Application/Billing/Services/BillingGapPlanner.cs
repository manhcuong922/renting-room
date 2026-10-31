using renting_room.Domain.Billing;
using renting_room.Domain.Common;
using renting_room.Domain.Contracts;
using renting_room.Domain.Properties;

namespace renting_room.Application.Billing;

/// <summary>Kết quả xét các kỳ chưa lập liền trước một kỳ cần lập.</summary>
/// <param name="Unbilled">Các kỳ chưa có phiếu liền trước kỳ cần lập (rỗng ⇒ chuỗi liền).</param>
/// <param name="Skip">true ⇒ phải ghi khoảng ngắt phủ <see cref="Unbilled"/> (cờ <c>skipUnbilledPeriods</c> đã bật và đủ điều kiện).</param>
/// <param name="Error">Lý do chặn tạo khoảng ngắt (BL-BR-43).</param>
/// <param name="ClosingPeriodStart">Đầu kỳ cuối của khoảng ngắt liền trước kỳ cần lập (đã có hoặc sắp ghi) — chỉ số <c>Periodic</c> của kỳ này là chỉ số đầu.</param>
public sealed record GapPlan(IReadOnlyList<BillingPeriod> Unbilled, bool Skip, Error? Error, DateOnly? ClosingPeriodStart)
{
    public static readonly GapPlan Continuous = new([], false, null, null);

    public bool NeedsSkip => Unbilled.Count > 0 && !Skip && Error is null;

    public IReadOnlyList<UnbilledPeriodDto> UnbilledDtos => Unbilled.Select(p => new UnbilledPeriodDto(p.Start, p.End)).ToList();
}

/// <summary>M07 BL-BR-42, 43: xác định kỳ chưa lập liền trước và điều kiện ghi khoảng ngắt tính tiền.</summary>
public sealed class BillingGapPlanner(IBillingGapRepository gaps)
{
    public async Task<GapPlan> PlanAsync(
        Contract contract, BillingSchedule schedule, BillingPeriod period, IReadOnlyCollection<(DateOnly Start, DateOnly End, InvoiceStatus Status)> invoices,
        bool skipUnbilledPeriods, CancellationToken ct)
    {
        if (period.Start <= contract.BillingStartDate)
            return GapPlan.Continuous;

        var existing = await gaps.ListByContractAsync(contract.Id, ct);
        var before = contract.BillingPeriods(schedule, period.Start).Where(p => p.End < period.Start).ToList();
        var unbilled = new List<BillingPeriod>();
        for (var i = before.Count - 1; i >= 0 && !IsCovered(before[i], invoices, existing); i--)
            unbilled.Insert(0, before[i]);

        if (unbilled.Count == 0)
            return new GapPlan([], false, null, existing.FirstOrDefault(g => g.ToDate == period.Start.AddDays(-1))?.ClosingPeriodStart);
        if (!skipUnbilledPeriods)
            return new GapPlan(unbilled, false, null, null);

        var error = Validate(schedule, period, unbilled, invoices);
        return error is null ? new GapPlan(unbilled, true, null, unbilled[^1].Start) : new GapPlan(unbilled, false, error, null);
    }

    private static bool IsCovered(
        BillingPeriod p, IReadOnlyCollection<(DateOnly Start, DateOnly End, InvoiceStatus Status)> invoices, IReadOnlyList<ContractBillingGap> gaps) =>
        invoices.Any(i => i.Start <= p.End && i.End >= p.Start) || gaps.Any(g => g.FromDate <= p.Start && p.End <= g.ToDate);

    private static Error? Validate(
        BillingSchedule schedule, BillingPeriod period, IReadOnlyList<BillingPeriod> unbilled,
        IReadOnlyCollection<(DateOnly Start, DateOnly End, InvoiceStatus Status)> invoices)
    {
        if (schedule.ChargeModeOn(period.Start) != ChargeMode.Postpaid || unbilled.Any(p => schedule.ChargeModeOn(p.Start) != ChargeMode.Postpaid))
            return BillingErrors.GapNotSupportedPrepaid;
        if (unbilled.Count > ContractBillingGap.MaxPeriods)
            return BillingErrors.GapTooLong;
        if (invoices.Where(i => i.End < unbilled[0].Start).OrderByDescending(i => i.Start).Select(i => (InvoiceStatus?)i.Status).FirstOrDefault()
            == InvoiceStatus.Draft)
            return BillingErrors.GapPreviousNotFinalized;
        if (schedule.Entries.Any(e => e.EffectiveFrom > DateOnly.MinValue && e.EffectiveFrom >= unbilled[0].Start && e.EffectiveFrom <= unbilled[^1].End))
            return BillingErrors.GapSpansScheduleChange;
        return null;
    }
}
