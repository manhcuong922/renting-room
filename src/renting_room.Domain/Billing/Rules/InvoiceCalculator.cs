using renting_room.Domain.Contracts;
using renting_room.Domain.Fees;
using renting_room.Domain.Meters;
using renting_room.Domain.Properties;

namespace renting_room.Domain.Billing;

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
        var factor = input.Schedule.RentFactor(period.Start, period.End, input.ProrationMode);

        if (input.Final is { RentBilled: { } billed })
            WarnRentOverpaid(contract, period, factor, billed, issues);
        else
            AddRent(contract, period, factor, input.Schedule.StandardPeriodContaining(period.Start), lines, issues);
        if (input.Final is not { LastPeriodHasRegular: true })
            AddServices(input, lines, issues);
        WarnTwoRentPeriods(input, issues);
        AddMetered(input, lines, segments, issues);
        return new InvoiceCalculation(lines.OrderBy(l => l.SortOrder).ToList(), segments, issues, input.RoundTotal);
    }

    /// <summary>
    /// BL-BR-03: tiền phòng hằng tháng = giá thuê hiệu lực tại đầu kỳ × hệ số prorate C-05 (phiếu quyết toán: tới ngày trả phòng).
    /// Kỳ chuyển tiếp (BL-BR-28): 1 tháng ± số ngày chủ trọ chọn, ghi rõ trên dòng.
    /// </summary>
    private static void AddRent(
        Contract contract, BillingPeriod period, decimal factor, StandardPeriod standard, List<CalculatedLine> lines, List<InvoiceIssue> issues)
    {
        if (contract.CurrentRent(period.Start) is not { } rent)
        {
            issues.Add(new InvoiceIssue("RENT_TERM_MISSING", InvoiceIssue.Error, "Không có giá thuê hiệu lực tại đầu kỳ."));
            return;
        }
        var name = standard.IsTransition && standard.AdjustDays != 0
            ? $"Tiền phòng (1 tháng {(standard.AdjustDays > 0 ? "+" : "−")} {Math.Abs(standard.AdjustDays)} ngày — đổi ngày chốt)"
            : "Tiền phòng";
        lines.Add(new CalculatedLine(InvoiceLineType.Rent, null, name, "tháng", period.Start, period.End, 1, rent,
            factor, Invoice.Money(rent * factor), 0));
    }

    private static DateOnly Max(DateOnly a, DateOnly b) => a > b ? a : b;

    private static DateOnly Min(DateOnly a, DateOnly b) => a < b ? a : b;

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

    /// <summary>
    /// BL-BR-28 (4): kỳ đầu thu trước ngay sau kỳ thu sau ⇒ người thuê trả tiền phòng 2 tháng gần như cùng lúc (phiếu thu sau tháng trước +
    /// phiếu thu trước tháng này) — nhắc chủ trọ báo trước.
    /// </summary>
    private static void WarnTwoRentPeriods(InvoiceCalcInput input, List<InvoiceIssue> issues)
    {
        if (input.Final is not null || input.Schedule.ChargeModeOn(input.Period.Start) != ChargeMode.Prepaid)
            return;
        var previous = input.Contract.BillingPeriods(input.Schedule, input.Period.Start).LastOrDefault(p => p.End < input.Period.Start);
        if (previous is not null && input.Schedule.ChargeModeOn(previous.Start) == ChargeMode.Postpaid)
            issues.Add(new InvoiceIssue("TWO_RENT_PERIODS", InvoiceIssue.Warning,
                "Khu vừa đổi sang thu trước: phiếu này thu trước tiền phòng tháng này, trong khi phiếu tháng trước (thu sau) cũng vừa thu — " +
                "người thuê trả 2 tháng tiền phòng gần nhau, nên báo trước."));
    }

    /// <summary>BL-BR-04: dịch vụ <b>thu trọn tháng</b>, kể cả kỳ lẻ / phiếu quyết toán (L1) — chủ trọ sửa số lượng / thành tiền trên nháp.</summary>
    private static void AddServices(InvoiceCalcInput input, List<CalculatedLine> lines, List<InvoiceIssue> issues)
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
                price.Value, null, Invoice.Money(quantity * price.Value), 100 + type.SortOrder));
        }
    }

    private static void AddMetered(InvoiceCalcInput input, List<CalculatedLine> lines, List<CalculatedSegment> segments, List<InvoiceIssue> issues)
    {
        IReadOnlyList<UsagePeriod> usages = input.Final is { } final
            ? UsagePeriod.ForFinal(input.Contract, input.Period, final.LastPeriodHasRegular, input.Schedule)
            : UsagePeriod.For(input.Contract, input.Period, input.Schedule) is { } regular ? [regular] : [];
        // Chuỗi chỉ số theo công tơ (MT-BR-12): kỳ sử dụng sau trong cùng phiếu nối tiếp đoạn đo của kỳ trước.
        var chain = input.LastSegmentEndReadings.ToDictionary(x => x.Key, x => x.Value);
        foreach (var usage in usages)
            AddMeteredPeriod(input, usage, usages.Count > 1, chain, lines, segments, issues);
    }

    /// <summary>
    /// Một kỳ sử dụng: mỗi khoản điện nước 1 dòng = Σ đoạn đo của mọi (lần ở, công tơ) giao kỳ — phòng đã rời tính tới chỉ số cuối lúc chuyển,
    /// phòng mới từ chỉ số nhận phòng (CT-BR-47), công tơ đã thay cộng cả bản cũ (MT-BR-15); giá / bậc tính một lần trên tổng (BL-BR-05).
    /// </summary>
    private static void AddMeteredPeriod(InvoiceCalcInput input, UsagePeriod usage, bool labelPeriod, Dictionary<Guid, Guid> chain,
        List<CalculatedLine> lines, List<CalculatedSegment> segments, List<InvoiceIssue> issues)
    {
        var contractId = input.Contract.Id;
        var allStays = input.Contract.RoomStays;
        var current = allStays[^1];
        var groups = allStays.Where(s => s.Overlaps(usage.Start, usage.End))
            .SelectMany(stay => input.RoomMeters
                .Where(m => m.RoomId == stay.RoomId && m.Overlaps(Max(usage.Start, stay.From), Min(usage.End, stay.To ?? usage.End))
                    && input.FeeTypes.TryGetValue(m.FeeTypeId, out var t) && t.Group == FeeGroup.Metered)
                .Select(meter => (Stay: stay, Meter: meter)))
            .GroupBy(x => x.Meter.FeeTypeId);
        foreach (var group in groups)
        {
            var type = input.FeeTypes[group.Key];
            var parts = new List<CalculatedSegment>();
            var complete = true;
            foreach (var (stay, meter) in group.OrderBy(x => x.Stay.From).ThenBy(x => x.Meter.InstalledDate))
            {
                var ownStay = allStays.Count > 1 ? stay : null;
                var start = usage.StartReading(meter, chain.TryGetValue(meter.Id, out var last) ? last : null, contractId, ownStay);
                var end = usage.EndReading(meter, contractId, ownStay, stay == current ? null : stay);
                var name = meter.SerialNo is null ? type.Name : $"{type.Name} ({meter.SerialNo})";
                if (start is null || end is null)
                {
                    issues.Add(new InvoiceIssue("MISSING_READING", InvoiceIssue.Error,
                        $"Thiếu chỉ số {(start is null ? "đầu" : usage.EndsWithFinal ? "cuối hợp đồng" : "cuối kỳ")} của công tơ {name}.", meter.Id));
                    complete = false;
                    continue;
                }
                if (end.Value < start.Value)
                {
                    issues.Add(new InvoiceIssue("READING_ORDER_INVALID", InvoiceIssue.Error,
                        $"Chỉ số cuối ({end.Value:0.##}) nhỏ hơn chỉ số đầu ({start.Value:0.##}) của công tơ {name}.", meter.Id));
                    complete = false;
                    continue;
                }
                chain[meter.Id] = end.Id;
                // Cùng một chỉ số (đã tính tới mốc này ở kỳ trước) hoặc đoạn đã thu bằng khoản chốt khi chuyển phòng (dữ liệu cũ) ⇒ không tính.
                if (end.Id == start.Id || input.ClosedReadingIds?.Contains(end.Id) == true)
                    continue;
                parts.Add(new CalculatedSegment(type.Id, meter.Id, meter.SerialNo, start.Id, end.Id, start.Value, end.Value,
                    meter.RoomId, input.RoomCodes?.GetValueOrDefault(meter.RoomId), start.ReadingDate, end.ReadingDate, usage.Start));
            }
            if (!complete || parts.Count == 0)
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
            var recent = input.RecentUsage?.GetValueOrDefault(type.Id) ?? [];
            if (UsageAnomaly.Check(type.Name, type.Unit, quantity, recent, input.Contract.OccupantsOn(usage.End).Any()) is { } unusual)
                issues.Add(new InvoiceIssue(UsageAnomaly.Code, InvoiceIssue.Warning, unusual, type.Id));
            var label = price.IsTiered ? $"{type.Name} (giá bậc)" : type.Name;
            // Phiếu có 2 kỳ sử dụng ⇒ ghi khoảng ngày trên từng dòng để người thuê đối chiếu.
            if (labelPeriod)
                label += $" {usage.Start:dd/MM}–{usage.End:dd/MM}";
            lines.Add(new CalculatedLine(InvoiceLineType.Metered, type.Id, label, type.Unit,
                usage.Start, usage.End, quantity, unitPrice, price.IsTiered && quantity > 0 ? amount / (quantity * unitPrice) : null, amount,
                10 + type.SortOrder));
        }
    }
}
