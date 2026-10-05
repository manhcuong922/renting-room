using renting_room.Domain.Contracts;
using renting_room.Domain.Fees;
using renting_room.Domain.Meters;
using renting_room.Domain.Properties;

namespace renting_room.Domain.Billing;

/// <summary>
/// Kỳ sử dụng điện nước của một phiếu (BL-BR-05): <c>Postpaid</c> = kỳ của phiếu; <c>Prepaid</c> = kỳ liền trước (kỳ đầu không có).
/// Phiếu quyết toán (BL-BR-17) kết thúc bằng chỉ số cuối HĐ — xem <see cref="ForFinal"/>.
/// </summary>
/// <param name="ClosingPeriodStart">Khóa của chỉ số cuối kỳ <c>Periodic</c> (MT-BR-04).</param>
public sealed record UsagePeriod(DateOnly Start, DateOnly End, DateOnly ClosingPeriodStart, bool EndsWithFinal)
{
    public static UsagePeriod? For(Contract contract, BillingPeriod period)
    {
        if (contract.ChargeMode == ChargeMode.Postpaid)
            return new UsagePeriod(period.Start, period.End, period.Start, false);

        var previous = contract.BillingPeriods(period.Start).LastOrDefault(p => p.End < period.Start);
        return previous is null ? null : new UsagePeriod(previous.Start, previous.End, previous.Start, false);
    }

    /// <summary>
    /// Phiếu quyết toán: điện nước tới chỉ số cuối HĐ. Bắt đầu từ đầu kỳ cuối; Prepaid mà kỳ cuối chưa có phiếu thường thì gồm cả kỳ trước
    /// (chưa ai thu). Chỉ số đầu vẫn nối chuỗi từ đoạn đo đã lập phiếu (MT-BR-12) nên không thu trùng.
    /// </summary>
    public static UsagePeriod ForFinal(Contract contract, BillingPeriod lastPeriod, bool lastPeriodHasRegular)
    {
        var start = lastPeriod.Start;
        if (contract.ChargeMode == ChargeMode.Prepaid && !lastPeriodHasRegular
            && contract.BillingPeriods(lastPeriod.Start).LastOrDefault(p => p.End < lastPeriod.Start) is { } previous)
            start = previous.Start;
        return new UsagePeriod(start, lastPeriod.End, start, true);
    }

    /// <summary>
    /// M06 §3.3 — chỉ số đầu: số cuối của đoạn đo gần nhất trên phiếu chưa hủy của HĐ (MT-BR-12); chưa có phiếu nào ⇒ chỉ số lắp
    /// (công tơ lắp trong kỳ) hoặc chỉ số gần nhất ≤ đầu kỳ (nhận phòng / tháng trước — phiếu đầu tiên của HĐ nhập từ trước).
    /// </summary>
    public MeterReading? StartReading(Meter meter, Guid? lastSegmentEndReadingId) =>
        lastSegmentEndReadingId is { } id ? meter.Find(id)
        : meter.InstalledDate > Start ? meter.Ordered.FirstOrDefault(r => r.Kind == ReadingKind.Initial)
        : meter.LatestOnOrBefore(Start);

    /// <summary>Chỉ số cuối: tháo công tơ trong kỳ ⇒ số tháo (MT-BR-15); kỳ cuối ⇒ chỉ số cuối HĐ; còn lại ⇒ chỉ số cuối kỳ.</summary>
    public MeterReading? EndReading(Meter meter, Guid contractId) =>
        meter.RemovedDate is { } removed && removed <= End ? meter.Removal
        : EndsWithFinal ? meter.FindFinal(contractId) ?? meter.FindPeriodic(contractId, ClosingPeriodStart)
        : meter.FindPeriodic(contractId, ClosingPeriodStart);
}

/// <param name="FeeTypes">Dịch vụ của HĐ + điện nước có công tơ ở phòng, kèm bảng giá.</param>
/// <param name="LastSegmentEndReadings">Theo công tơ: chỉ số cuối của đoạn đo gần nhất trên phiếu chưa hủy (kỳ trước) của HĐ.</param>
/// <param name="Final">Có ⇒ tính phiếu quyết toán (BL-BR-17); null ⇒ phiếu thường.</param>
public sealed record InvoiceCalcInput(
    Contract Contract,
    BillingPeriod Period,
    IReadOnlyDictionary<Guid, FeeType> FeeTypes,
    IReadOnlyList<Meter> RoomMeters,
    IReadOnlyDictionary<Guid, Guid> LastSegmentEndReadings,
    FinalSettlement? Final = null);

/// <summary>
/// Bối cảnh phiếu quyết toán: <paramref name="RentCovered"/> = tiền phòng của kỳ cuối đã nằm trên phiếu trước (phiếu thường / chu kỳ nhiều tháng)
/// ⇒ không thu thêm, cũng không hoàn (hoàn tiền: để sau); <paramref name="LastPeriodHasRegular"/> = kỳ cuối đã có phiếu thường ⇒ dịch vụ đã thu.
/// </summary>
public sealed record FinalSettlement(bool RentCovered, bool LastPeriodHasRegular);

/// <summary>
/// M07 — tính phần hệ thống của phiếu (thuần, không DB): Tiền phòng (BL-BR-03), Dịch vụ (BL-BR-04), Điện nước (BL-BR-05).
/// Thiếu giá / chỉ số / giá thuê ⇒ <see cref="InvoiceIssue"/> mức Error (chặn chốt — BL-BR-11).
/// </summary>
public static class InvoiceCalculator
{
    public static InvoiceCalculation Calculate(InvoiceCalcInput input)
    {
        var contract = input.Contract;
        var period = input.Period;
        var lines = new List<CalculatedLine>();
        var segments = new List<CalculatedSegment>();
        var issues = new List<InvoiceIssue>();
        var factor = contract.ProrationMode == ProrationMode.Daily
            ? BillingPeriodCalculator.ProrationFactor(period.Start, period.End, contract.BillingAnchorDay)
            : 1m;

        if (input.Final is not { RentCovered: true })
            AddRent(contract, period, input.Final is not null, lines, issues);
        if (input.Final is not { LastPeriodHasRegular: true })
            AddServices(input, factor, lines, issues);
        AddMetered(input, lines, segments, issues);
        return new InvoiceCalculation(lines.OrderBy(l => l.SortOrder).ToList(), segments, issues);
    }

    /// <summary>
    /// BL-BR-03 / BL-BR-26: phiếu thường — tiền phòng chỉ ở tháng đầu chu kỳ, = Σ (giá thuê đầu từng kỳ × hệ số kỳ) cho N kỳ của chu kỳ;
    /// chu kỳ cắt tại <c>end_date</c> nếu ngày hết hạn rơi trong chu kỳ (quá hạn / ở tiếp thì không cắt). Phiếu quyết toán — chỉ kỳ cuối.
    /// </summary>
    private static void AddRent(Contract contract, BillingPeriod period, bool isFinal, List<CalculatedLine> lines, List<InvoiceIssue> issues)
    {
        var cycle = new List<BillingPeriod> { period };
        if (!isFinal && contract.RentCycleMonths > 1)
        {
            var all = BillingPeriodCalculator.Periods(contract.StartDate, contract.ActualEndDate, contract.BillingAnchorDay,
                period.Start.AddMonths(contract.RentCycleMonths + 1)).ToList();
            var index = all.FindIndex(p => p.Start == period.Start);
            if (index % contract.RentCycleMonths != 0)
                return; // tháng 2, 3… của chu kỳ: chỉ điện nước, dịch vụ
            DateOnly? cutoff = contract.EndDate is { } end && end >= period.Start ? end : null;
            cycle = all.Skip(index).Take(contract.RentCycleMonths)
                .Where(p => cutoff is null || p.Start <= cutoff)
                .Select(p => cutoff is { } c && p.End > c ? new BillingPeriod(p.Start, c) : p)
                .ToList();
        }

        var rents = cycle.Select(p => contract.CurrentRent(p.Start)).ToList();
        if (rents.Any(r => r is null))
        {
            issues.Add(new InvoiceIssue("RENT_TERM_MISSING", InvoiceIssue.Error, "Không có giá thuê hiệu lực tại đầu kỳ."));
            return;
        }

        var weighted = cycle.Zip(rents, (p, r) => r!.Value * Factor(contract, p)).Sum();
        var baseRent = rents[0]!.Value;
        var count = cycle.Count;
        var description = count == 1 ? "Tiền phòng" : $"Tiền phòng {count} tháng ({cycle[0].Start:dd/MM}–{cycle[^1].End:dd/MM})";
        lines.Add(new CalculatedLine(InvoiceLineType.Rent, null, description, "tháng", cycle[0].Start, cycle[^1].End, count, baseRent,
            weighted / (baseRent * count), Invoice.Money(weighted), 0));
    }

    private static decimal Factor(Contract contract, BillingPeriod period) =>
        contract.ProrationMode == ProrationMode.Daily
            ? BillingPeriodCalculator.ProrationFactor(period.Start, period.End, contract.BillingAnchorDay)
            : 1m;

    private static void AddServices(InvoiceCalcInput input, decimal factor, List<CalculatedLine> lines, List<InvoiceIssue> issues)
    {
        var (contract, period) = (input.Contract, input.Period);
        foreach (var fee in contract.Fees.Where(f => f.Covers(period.Start)))
        {
            if (!input.FeeTypes.TryGetValue(fee.FeeTypeId, out var type) || type.Group != FeeGroup.Service)
                continue;

            var quantity = type.ChargeBasis switch
            {
                ChargeBasis.PerOccupant => contract.OccupantsOn(period.Start).Count(),
                ChargeBasis.PerUnit => fee.Quantity,
                _ => 1m
            };
            var price = fee.UnitPriceOverride ?? type.ResolvePrice(period.Start)?.UnitPrice;
            if (price is null)
            {
                issues.Add(new InvoiceIssue("FEE_PRICE_MISSING", InvoiceIssue.Error, $"\"{type.Name}\" chưa có giá tại đầu kỳ.", type.Id));
                continue;
            }
            lines.Add(new CalculatedLine(InvoiceLineType.Service, type.Id, type.Name, type.Unit, period.Start, period.End, quantity,
                price.Value, factor, Invoice.Money(quantity * price.Value * factor), 100 + type.SortOrder));
        }
    }

    private static void AddMetered(InvoiceCalcInput input, List<CalculatedLine> lines, List<CalculatedSegment> segments, List<InvoiceIssue> issues)
    {
        var usage = input.Final is { } final
            ? UsagePeriod.ForFinal(input.Contract, input.Period, final.LastPeriodHasRegular)
            : UsagePeriod.For(input.Contract, input.Period);
        if (usage is null)
            return;

        var groups = input.RoomMeters
            .Where(m => m.Overlaps(usage.Start, usage.End)
                && input.FeeTypes.TryGetValue(m.FeeTypeId, out var t) && t.Group == FeeGroup.Metered)
            .GroupBy(m => m.FeeTypeId);
        foreach (var group in groups)
        {
            var type = input.FeeTypes[group.Key];
            var parts = new List<CalculatedSegment>();
            foreach (var meter in group.OrderBy(m => m.InstalledDate))
            {
                var start = usage.StartReading(meter, input.LastSegmentEndReadings.TryGetValue(meter.Id, out var last) ? last : null);
                var end = usage.EndReading(meter, input.Contract.Id);
                var name = meter.SerialNo is null ? type.Name : $"{type.Name} ({meter.SerialNo})";
                if (start is null || end is null)
                {
                    issues.Add(new InvoiceIssue("MISSING_READING", InvoiceIssue.Error,
                        $"Thiếu chỉ số {(start is null ? "đầu" : usage.EndsWithFinal ? "cuối hợp đồng" : "cuối kỳ")} của công tơ {name}.", meter.Id));
                    continue;
                }
                if (end.Value < start.Value)
                {
                    issues.Add(new InvoiceIssue("READING_ORDER_INVALID", InvoiceIssue.Error,
                        $"Chỉ số cuối ({end.Value:0.##}) nhỏ hơn chỉ số đầu ({start.Value:0.##}) của công tơ {name}.", meter.Id));
                    continue;
                }
                parts.Add(new CalculatedSegment(type.Id, meter.Id, meter.SerialNo, start.Id, end.Id, start.Value, end.Value));
            }
            if (parts.Count != group.Count())
                continue;

            // Giá mới: bản giá hiệu lực tại ngày cuối kỳ sử dụng — đổi giá giữa kỳ thì cả kỳ tính giá mới (BL-BR-05).
            var price = type.ResolvePrice(usage.End);
            if (price is null)
            {
                issues.Add(new InvoiceIssue("FEE_PRICE_MISSING", InvoiceIssue.Error, $"\"{type.Name}\" chưa có giá.", type.Id));
                continue;
            }
            var quantity = parts.Sum(s => s.EndValue - s.StartValue);
            var amount = price.Amount(quantity);
            // Theo bậc: đơn giá hiển thị = giá bình quân (thành tiền / sản lượng) — FE-BR-15.
            var unitPrice = price.IsTiered && quantity > 0 ? decimal.Round(amount / quantity, 2) : price.UnitPrice;
            segments.AddRange(parts);
            lines.Add(new CalculatedLine(InvoiceLineType.Metered, type.Id, price.IsTiered ? $"{type.Name} (giá bậc)" : type.Name, type.Unit,
                usage.Start, usage.End, quantity, unitPrice, price.IsTiered && quantity > 0 ? amount / (quantity * unitPrice) : null, amount,
                10 + type.SortOrder));
        }
    }
}
