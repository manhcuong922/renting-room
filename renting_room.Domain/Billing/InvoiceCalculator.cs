using renting_room.Domain.Contracts;
using renting_room.Domain.Fees;
using renting_room.Domain.Meters;
using renting_room.Domain.Properties;

namespace renting_room.Domain.Billing;

/// <summary>
/// Kỳ sử dụng điện nước của một phiếu (BL-BR-05): <c>Postpaid</c> = kỳ của phiếu; <c>Prepaid</c> = kỳ liền trước (kỳ đầu không có).
/// Cách thu lấy theo lịch kỳ thu của khu tại từng kỳ (K4 — BL-BR-28: đổi thu trước ↔ thu sau không thu trùng / sót điện nước).
/// Phiếu quyết toán (BL-BR-17) kết thúc bằng chỉ số cuối HĐ — xem <see cref="ForFinal"/>.
/// </summary>
/// <param name="ClosingPeriodStart">Khóa của chỉ số cuối kỳ <c>Periodic</c> (MT-BR-04).</param>
public sealed record UsagePeriod(DateOnly Start, DateOnly End, DateOnly ClosingPeriodStart, bool EndsWithFinal)
{
    public static UsagePeriod? For(Contract contract, BillingPeriod period, BillingSchedule schedule)
    {
        var previous = contract.BillingPeriods(schedule, period.Start).LastOrDefault(p => p.End < period.Start);
        var previousMode = previous is null ? (ChargeMode?)null : schedule.ChargeModeOn(previous.Start);
        if (schedule.ChargeModeOn(period.Start) == ChargeMode.Postpaid)
        {
            // Vừa đổi thu trước → thu sau: điện nước kỳ trước chưa ai thu ⇒ gộp vào kỳ này.
            var start = previousMode == ChargeMode.Prepaid ? previous!.Start : period.Start;
            return new UsagePeriod(start, period.End, period.Start, false);
        }

        // Thu trước: điện nước kỳ liền trước — trừ khi kỳ đó thu sau (đã thu trên phiếu của chính nó).
        return previous is null || previousMode == ChargeMode.Postpaid
            ? null
            : new UsagePeriod(previous.Start, previous.End, previous.Start, false);
    }

    /// <summary>
    /// Phiếu quyết toán: điện nước tới chỉ số cuối HĐ. Bắt đầu từ đầu kỳ cuối; Prepaid mà kỳ cuối chưa có phiếu thường thì gồm cả kỳ trước
    /// (chưa ai thu). Chỉ số đầu vẫn nối chuỗi từ đoạn đo đã lập phiếu (MT-BR-12) nên không thu trùng.
    /// </summary>
    public static UsagePeriod ForFinal(Contract contract, BillingPeriod lastPeriod, bool lastPeriodHasRegular, BillingSchedule schedule)
    {
        var start = lastPeriod.Start;
        if (!lastPeriodHasRegular
            && contract.BillingPeriods(schedule, lastPeriod.Start).LastOrDefault(p => p.End < lastPeriod.Start) is { } previous
            && schedule.ChargeModeOn(previous.Start) == ChargeMode.Prepaid)
            start = previous.Start;
        return new UsagePeriod(start, lastPeriod.End, start, true);
    }

    /// <summary>
    /// M06 §3.3 — chỉ số đầu: số cuối của đoạn đo gần nhất trên phiếu chưa hủy của HĐ (MT-BR-12); chưa có phiếu nào ⇒ chỉ số lắp
    /// (công tơ lắp trong kỳ) hoặc chỉ số gần nhất ≤ đầu kỳ (nhận phòng / tháng trước — phiếu đầu tiên của HĐ nhập từ trước).
    /// </summary>
    /// <param name="stay">Khoảng HĐ ở phòng của công tơ — chuyển tới phòng giữa kỳ (CT-BR-47) ⇒ bắt đầu từ chỉ số nhận phòng của HĐ.</param>
    public MeterReading? StartReading(Meter meter, Guid? lastSegmentEndReadingId, Guid contractId, RoomStay? stay = null) =>
        lastSegmentEndReadingId is { } id ? meter.Find(id)
        : stay is not null && stay.From > Start ? meter.FindHandover(contractId)
        : meter.InstalledDate > Start ? meter.Ordered.FirstOrDefault(r => r.Kind == ReadingKind.Initial)
        : meter.LatestOnOrBefore(Start);

    /// <summary>
    /// Chỉ số cuối: tháo công tơ trong kỳ ⇒ số tháo (MT-BR-15); kỳ cuối hoặc HĐ chuyển khỏi phòng trong kỳ (CT-BR-47) ⇒ chỉ số cuối HĐ;
    /// còn lại ⇒ chỉ số cuối kỳ.
    /// </summary>
    public MeterReading? EndReading(Meter meter, Guid contractId, RoomStay? stay = null) =>
        meter.RemovedDate is { } removed && removed <= End ? meter.Removal
        : stay is { To: { } left } && left < End && !EndsWithFinal ? meter.FindFinal(contractId)
        : EndsWithFinal ? meter.FindFinal(contractId) ?? meter.FindPeriodic(contractId, ClosingPeriodStart)
        : meter.FindPeriodic(contractId, ClosingPeriodStart);
}

/// <param name="FeeTypes">Dịch vụ của HĐ + điện nước có công tơ ở phòng, kèm bảng giá.</param>
/// <param name="LastSegmentEndReadings">Theo công tơ: chỉ số cuối của đoạn đo gần nhất trên phiếu chưa hủy (kỳ trước) của HĐ.</param>
/// <param name="Schedule">Lịch kỳ thu của khu (ngày chốt, thu trước / thu sau theo từng kỳ — PR-BR-09).</param>
/// <param name="ProrationMode">Cách tính tiền phòng kỳ lẻ của khu.</param>
/// <param name="Final">Có ⇒ tính phiếu quyết toán (BL-BR-17); null ⇒ phiếu thường.</param>
/// <param name="RecentUsage">Theo khoản điện nước: sản lượng các phiếu thường trước của HĐ, mới nhất trước (MT-BR-08).</param>
/// <param name="RoundTotal">Khu bật làm tròn tổng phiếu xuống nghìn (BL-BR-29).</param>
public sealed record InvoiceCalcInput(
    Contract Contract,
    BillingPeriod Period,
    BillingSchedule Schedule,
    ProrationMode ProrationMode,
    IReadOnlyDictionary<Guid, FeeType> FeeTypes,
    IReadOnlyList<Meter> RoomMeters,
    IReadOnlyDictionary<Guid, Guid> LastSegmentEndReadings,
    FinalSettlement? Final = null,
    IReadOnlyDictionary<Guid, IReadOnlyList<decimal>>? RecentUsage = null,
    bool RoundTotal = false);

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
        var usage = input.Final is { } final
            ? UsagePeriod.ForFinal(input.Contract, input.Period, final.LastPeriodHasRegular, input.Schedule)
            : UsagePeriod.For(input.Contract, input.Period, input.Schedule);
        if (usage is null)
            return;

        // CT-BR-47: công tơ của mọi phòng HĐ đã ở trong kỳ sử dụng (chuyển phòng giữa kỳ ⇒ 2 phòng).
        var stays = input.Contract.RoomStays.Where(s => s.Overlaps(usage.Start, usage.End)).ToList();
        RoomStay? StayOf(Meter m) => stays.LastOrDefault(s => s.RoomId == m.RoomId);
        var groups = input.RoomMeters
            .Where(m => StayOf(m) is { } stay && m.Overlaps(Max(usage.Start, stay.From), Min(usage.End, stay.To ?? usage.End))
                && input.FeeTypes.TryGetValue(m.FeeTypeId, out var t) && t.Group == FeeGroup.Metered)
            .GroupBy(m => m.FeeTypeId);
        foreach (var group in groups)
        {
            var type = input.FeeTypes[group.Key];
            var parts = new List<CalculatedSegment>();
            foreach (var meter in group.OrderBy(m => m.InstalledDate))
            {
                var stay = stays.Count > 1 ? StayOf(meter) : null;
                var start = usage.StartReading(meter, input.LastSegmentEndReadings.TryGetValue(meter.Id, out var last) ? last : null,
                    input.Contract.Id, stay);
                var end = usage.EndReading(meter, input.Contract.Id, stay);
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
            var recent = input.RecentUsage?.GetValueOrDefault(type.Id) ?? [];
            if (UsageAnomaly.Check(type.Name, type.Unit, quantity, recent, input.Contract.OccupantsOn(usage.End).Any()) is { } unusual)
                issues.Add(new InvoiceIssue(UsageAnomaly.Code, InvoiceIssue.Warning, unusual, type.Id));
            lines.Add(new CalculatedLine(InvoiceLineType.Metered, type.Id, price.IsTiered ? $"{type.Name} (giá bậc)" : type.Name, type.Unit,
                usage.Start, usage.End, quantity, unitPrice, price.IsTiered && quantity > 0 ? amount / (quantity * unitPrice) : null, amount,
                10 + type.SortOrder));
        }
    }
}
