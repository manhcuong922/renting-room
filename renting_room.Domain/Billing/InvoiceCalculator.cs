using renting_room.Domain.Contracts;
using renting_room.Domain.Fees;
using renting_room.Domain.Meters;
using renting_room.Domain.Properties;

namespace renting_room.Domain.Billing;

/// <summary>
/// Kỳ sử dụng điện nước của một phiếu (BL-BR-05): <c>Postpaid</c> = kỳ của phiếu; <c>Prepaid</c> = kỳ liền trước (kỳ đầu không có).
/// Kỳ cuối (chứa ngày trả phòng) kết thúc bằng chỉ số cuối HĐ; Prepaid gộp luôn điện nước của chính kỳ cuối (thay phiếu Final ở đợt 1).
/// </summary>
/// <param name="ClosingPeriodStart">Khóa của chỉ số cuối kỳ <c>Periodic</c> (MT-BR-04).</param>
public sealed record UsagePeriod(DateOnly Start, DateOnly End, DateOnly ClosingPeriodStart, bool EndsWithFinal)
{
    public static UsagePeriod? For(Contract contract, BillingPeriod period)
    {
        var isLast = contract.ActualEndDate is { } end && end <= period.End;
        if (contract.ChargeMode == ChargeMode.Postpaid)
            return new UsagePeriod(period.Start, period.End, period.Start, isLast);

        var previous = contract.BillingPeriods(period.Start).LastOrDefault(p => p.End < period.Start);
        if (previous is null)
            return isLast ? new UsagePeriod(period.Start, period.End, period.Start, true) : null;
        return isLast
            ? new UsagePeriod(previous.Start, period.End, previous.Start, true)
            : new UsagePeriod(previous.Start, previous.End, previous.Start, false);
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
public sealed record InvoiceCalcInput(
    Contract Contract,
    BillingPeriod Period,
    IReadOnlyDictionary<Guid, FeeType> FeeTypes,
    IReadOnlyList<Meter> RoomMeters,
    IReadOnlyDictionary<Guid, Guid> LastSegmentEndReadings);

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

        var rent = contract.CurrentRent(period.Start);
        if (rent is null)
            issues.Add(new InvoiceIssue("RENT_TERM_MISSING", InvoiceIssue.Error, "Không có giá thuê hiệu lực tại đầu kỳ."));
        else
            lines.Add(new CalculatedLine(InvoiceLineType.Rent, null, "Tiền phòng", "tháng", period.Start, period.End, 1, rent.Value,
                factor, Invoice.Money(rent.Value * factor), 0));

        AddServices(input, factor, lines, issues);
        AddMetered(input, lines, segments, issues);
        return new InvoiceCalculation(lines.OrderBy(l => l.SortOrder).ToList(), segments, issues);
    }

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
        var usage = UsagePeriod.For(input.Contract, input.Period);
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
            var price = type.ResolvePrice(usage.End)?.UnitPrice;
            if (price is null)
            {
                issues.Add(new InvoiceIssue("FEE_PRICE_MISSING", InvoiceIssue.Error, $"\"{type.Name}\" chưa có giá.", type.Id));
                continue;
            }
            var quantity = parts.Sum(s => s.EndValue - s.StartValue);
            segments.AddRange(parts);
            lines.Add(new CalculatedLine(InvoiceLineType.Metered, type.Id, type.Name, type.Unit, usage.Start, usage.End, quantity,
                price.Value, null, Invoice.Money(quantity * price.Value), 10 + type.SortOrder));
        }
    }
}
