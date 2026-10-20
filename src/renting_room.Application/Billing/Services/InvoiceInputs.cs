using System.Globalization;
using FluentValidation;
using renting_room.Application.Contracts;
using renting_room.Application.Fees;
using renting_room.Application.Meters;
using renting_room.Application.Properties;
using renting_room.Domain.Billing;
using renting_room.Domain.Common;
using renting_room.Domain.Contracts;

namespace renting_room.Application.Billing;

public static class BillingMonths
{
    public static DateOnly? Parse(string? value) =>
        DateOnly.TryParseExact($"{value}-01", "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var month) ? month : null;

    public static IRuleBuilderOptions<T, string?> BillingMonth<T>(this IRuleBuilder<T, string?> rule, TimeProvider clock) =>
        rule.Must(v => Parse(v) is { } m && m <= clock.GetUtcNow().ToBusinessDate().AddMonths(1))
            .WithErrorCode("INVALID_BILLING_MONTH").WithMessage("Tháng thu dạng yyyy-MM, tối đa tháng sau.");

    public static BillingPeriod? PeriodIn(Contract contract, DateOnly month, BillingSchedule schedule) =>
        contract.BillingPeriods(schedule, month.AddMonths(2)).FirstOrDefault(p => p.Month == month);
}

public sealed class InvoiceInputLoader(IPropertyQueries properties, IFeeQueries fees, IMeterQueries meters, IInvoiceQueries invoices)
{
    /// <param name="type"><c>Final</c> ⇒ kèm bối cảnh trả phòng (tiền phòng kỳ cuối đã thu chưa, kỳ cuối đã có phiếu thường chưa) và
    /// nối chuỗi chỉ số cả với phiếu thường của chính kỳ cuối.</param>
    public async Task<InvoiceCalcInput> LoadAsync(
        Contract contract, BillingPeriod period, Guid? excludeInvoiceId, CancellationToken ct, InvoiceType type = InvoiceType.Regular)
    {
        var isFinal = type == InvoiceType.Final;
        var billing = await properties.GetBillingAsync(contract.PropertyId, ct);
        var types = await ContractFeeRules.LoadTypesAsync(fees, meters, contract, ct);
        var source = await invoices.LoadSourceDataAsync(contract, period.Start, excludeInvoiceId, isFinal, ct);
        var lastEnd = source.PreviousSegments.GroupBy(s => s.MeterId).ToDictionary(g => g.Key, g => g.MaxBy(s => s.PeriodStart).EndReadingId);

        FinalSettlement? final = null;
        if (isFinal)
        {
            var rentLines = source.FinalRentAmounts!;
            var charge = contract.FinalPeriodCharge ?? FinalPeriodCharge.FullPeriod;
            final = rentLines.Count == 0
                ? new FinalSettlement(null, source.HasRegularInFinalPeriod, charge)
                : await WithRentSourceAsync(rentLines, source.HasRegularInFinalPeriod, charge, ct);
        }
        var recent = source.RecentUsage
            .GroupBy(x => x.FeeTypeId)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<decimal>)g.OrderByDescending(x => x.PeriodStart).Select(x => x.Quantity).ToList());
        return new InvoiceCalcInput(contract, period, billing.Schedule, billing.ProrationMode, types, source.Meters.ToList(), lastEnd, final, recent,
            billing.RoundInvoiceTotal, source.ClosedReadingIds, source.RoomCodes);
    }

    /// <summary>Khu thu trước: tiền phòng kỳ cuối đã lập ở hóa đơn tháng ⇒ kèm số người thuê đã trả thật + đã hoàn để gợi ý hoàn đúng.</summary>
    private async Task<FinalSettlement> WithRentSourceAsync(
        IReadOnlyList<(Guid InvoiceId, decimal Amount)> rentLines, bool hasRegular, FinalPeriodCharge charge, CancellationToken ct)
    {
        var sourceId = rentLines[^1].InvoiceId;
        var source = await invoices.GetAsync(sourceId, ct);
        var collected = source is { Status: InvoiceStatus.Finalized } ? source.PaidAmount - source.WrittenOffAmount : 0;
        var refunded = -await invoices.GetRefundLinesTotalAsync(sourceId, null, ct);
        return new FinalSettlement(rentLines.Sum(l => l.Amount), hasRegular, charge, sourceId, collected, refunded);
    }

    public static BillingPeriod? CurrentPeriod(Contract contract, Invoice invoice, BillingSchedule schedule) =>
        contract.BillingPeriods(schedule, invoice.PeriodStart).FirstOrDefault(p => p.Start == invoice.PeriodStart);
}
