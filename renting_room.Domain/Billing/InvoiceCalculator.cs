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
/// Bối cảnh phiếu quyết toán: <paramref name="RentBilled"/> = tiền phòng kỳ cuối đã nằm trên phiếu thường (null = chưa thu) ⇒ không thu thêm;
/// thu nhiều hơn số ngày ở thực tế ⇒ cảnh báo <c>RENT_OVERPAID</c>, chủ trọ tự thêm dòng Hoàn trả (BL-BR-27).
/// <paramref name="LastPeriodHasRegular"/> = kỳ cuối đã có phiếu thường ⇒ dịch vụ đã thu.
/// </summary>
public sealed record FinalSettlement(decimal? RentBilled, bool LastPeriodHasRegular);

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

        if (input.Final is { RentBilled: { } billed })
            WarnRentOverpaid(contract, period, factor, billed, issues);
        else
            AddRent(contract, period, factor, lines, issues);
        if (input.Final is not { LastPeriodHasRegular: true })
            AddServices(input, factor, lines, issues);
        AddMetered(input, lines, segments, issues);
        return new InvoiceCalculation(lines.OrderBy(l => l.SortOrder).ToList(), segments, issues);
    }

    /// <summary>BL-BR-03: tiền phòng hằng tháng = giá thuê hiệu lực tại đầu kỳ × hệ số prorate C-05 (phiếu quyết toán: tới ngày trả phòng).</summary>
    private static void AddRent(Contract contract, BillingPeriod period, decimal factor, List<CalculatedLine> lines, List<InvoiceIssue> issues)
    {
        if (contract.CurrentRent(period.Start) is not { } rent)
        {
            issues.Add(new InvoiceIssue("RENT_TERM_MISSING", InvoiceIssue.Error, "Không có giá thuê hiệu lực tại đầu kỳ."));
            return;
        }
        lines.Add(new CalculatedLine(InvoiceLineType.Rent, null, "Tiền phòng", "tháng", period.Start, period.End, 1, rent,
            factor, Invoice.Money(rent * factor), 0));
    }

    /// <summary>
    /// BL-BR-17: kỳ cuối đã thu tiền phòng trên phiếu thường mà người thuê trả phòng sớm hơn ⇒ nhắc phần thu thừa để chủ trọ thêm dòng
    /// Hoàn trả nếu hoàn (không tự hoàn — BL-BR-27).
    /// </summary>
    private static void WarnRentOverpaid(Contract contract, BillingPeriod period, decimal factor, decimal billed, List<InvoiceIssue> issues)
    {
        if (contract.CurrentRent(period.Start) is not { } rent)
            return;
        var overpaid = billed - Invoice.Money(rent * factor);
        if (overpaid > 0)
            issues.Add(new InvoiceIssue("RENT_OVERPAID", InvoiceIssue.Warning,
                $"Tiền phòng kỳ cuối đã thu {billed:N0}đ, ở tới {period.End:dd/MM} tương ứng {billed - overpaid:N0}đ — thừa {overpaid:N0}đ. " +
                "Thêm dòng Hoàn trả nếu trả lại người thuê."));
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
            // Giá theo phiên bản: bản giá mới nhất hiệu lực tới ngày cuối kỳ, áp cả kỳ (FE-BR-10) — giá riêng của HĐ ưu tiên.
            var price = fee.UnitPriceOverride ?? type.ResolvePrice(period.End)?.UnitPrice;
            if (price is null)
            {
                issues.Add(new InvoiceIssue("FEE_PRICE_MISSING", InvoiceIssue.Error, $"\"{type.Name}\" chưa có giá.", type.Id));
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

            // Giá theo phiên bản: bản giá mới nhất hiệu lực tới ngày cuối kỳ sử dụng, áp cả kỳ (FE-BR-10, BL-BR-05).
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
