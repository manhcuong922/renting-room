using renting_room.Domain.Contracts;
using renting_room.Domain.Meters;
using renting_room.Domain.Properties;

namespace renting_room.Domain.Billing;

/// <param name="PreviousClosingPeriodStart">Đầu kỳ của HĐ kết thúc ngay trước <see cref="Start"/> — chỉ số chốt kỳ đó trên lưới là số đầu (M06 MT-BR-12, H-03).
/// null ⇒ kỳ đầu của HĐ, hoặc hóa đơn trả phòng khu thu sau đã có phiếu thường cùng kỳ (nối theo phiếu).</param>
public sealed record UsagePeriod(DateOnly Start, DateOnly End, DateOnly ClosingPeriodStart, bool EndsWithFinal, DateOnly? PreviousClosingPeriodStart = null)
{
    public static UsagePeriod? For(Contract contract, BillingPeriod period, BillingSchedule schedule)
    {
        var previous = contract.BillingPeriods(schedule, period.Start).LastOrDefault(p => p.End < period.Start);
        var previousMode = previous is null ? (ChargeMode?)null : schedule.ChargeModeOn(previous.Start);
        if (schedule.ChargeModeOn(period.Start) == ChargeMode.Postpaid)
        {
            var start = previousMode == ChargeMode.Prepaid ? previous!.Start : period.Start;
            return new UsagePeriod(start, period.End, period.Start, false, ClosingBefore(contract, schedule, start));
        }

        return previous is null || previousMode == ChargeMode.Postpaid
            ? null
            : new UsagePeriod(previous.Start, previous.End, previous.Start, false, ClosingBefore(contract, schedule, previous.Start));
    }

    public static IReadOnlyList<UsagePeriod> ForFinal(Contract contract, BillingPeriod lastPeriod, bool lastPeriodHasRegular, BillingSchedule schedule)
    {
        // Thu sau mà kỳ cuối đã có phiếu thường: phiếu đó đã tính điện tới chốt kỳ cuối ⇒ hóa đơn trả phòng nối tiếp phiếu đó, không lấy chốt kỳ trước.
        var last = new UsagePeriod(lastPeriod.Start, lastPeriod.End, lastPeriod.Start, true,
            lastPeriodHasRegular ? null : ClosingBefore(contract, schedule, lastPeriod.Start));
        return !lastPeriodHasRegular
            && contract.BillingPeriods(schedule, lastPeriod.Start).LastOrDefault(p => p.End < lastPeriod.Start) is { } previous
            && schedule.ChargeModeOn(previous.Start) == ChargeMode.Prepaid
                ? [new UsagePeriod(previous.Start, previous.End, previous.Start, false, ClosingBefore(contract, schedule, previous.Start)), last]
                : [last];
    }

    private static DateOnly? ClosingBefore(Contract contract, BillingSchedule schedule, DateOnly start) =>
        contract.BillingPeriods(schedule, start).LastOrDefault(p => p.End < start)?.Start;

    /// <summary>
    /// Kỳ chốt trên lưới làm số đầu (M06 §3.3 bước 2(g)): lần ở đang ở từ trước kỳ, công tơ lắp trước kỳ, HĐ có kỳ trước.
    /// Ngoài ra (kỳ đầu, vào phòng / lắp công tơ trong kỳ, phòng đã rời) ⇒ null, nối theo phiếu / chỉ số nhận phòng.
    /// </summary>
    public DateOnly? GridStartClosing(Meter meter, RoomStay? stay) =>
        PreviousClosingPeriodStart is { } closing && meter.InstalledDate < Start && (stay is null || stay.From < Start) ? closing : null;

    /// <param name="currentStay">false ⇒ lần ở đã rời (chuyển phòng): kỳ trước có thể kết thúc bằng chỉ số cuối lúc chuyển, nối theo phiếu.</param>
    public MeterReading? StartReading(Meter meter, Guid? lastSegmentEndReadingId, Guid contractId, RoomStay? stay = null, bool currentStay = true)
    {
        if (currentStay && GridStartClosing(meter, stay) is { } closing)
            return meter.FindPeriodic(contractId, closing);
        if (lastSegmentEndReadingId is { } id)
        {
            var chained = meter.Find(id);
            if (stay is null || chained is null || chained.ReadingDate >= stay.From)
                return chained;
        }
        var movedIn = stay is not null && stay.From > Start;
        if (meter.InstalledDate > (movedIn ? stay!.From : Start))
            return meter.Ordered.FirstOrDefault(r => r.Kind == ReadingKind.Initial);
        if (movedIn)
            return meter.FindHandover(contractId, stay!.From, stay.To);
        // Chỉ số nhận phòng của chính HĐ (M06 §3.3 bước 2(b)): không lấy chỉ số của người trước (mỗi HĐ có dải số riêng — MT-BR-03).
        return meter.FindHandover(contractId, to: Start) ?? meter.LatestOnOrBefore(Start);
    }

    public MeterReading? EndReading(Meter meter, Guid contractId, RoomStay? stay = null, RoomStay? left = null)
    {
        var until = left?.To is { } leftOn && leftOn < End ? leftOn : End;
        if (meter.RemovedDate is { } removed && removed <= until)
            return meter.Removal;
        if (left is not null)
            return left.To <= End
                ? meter.FindFinal(contractId, left.From, left.To)
                : meter.FindPeriodic(contractId, ClosingPeriodStart) ?? meter.FindFinal(contractId, left.From, left.To);
        return EndsWithFinal ? meter.FindFinal(contractId, stay?.From) ?? meter.FindPeriodic(contractId, ClosingPeriodStart)
            : meter.FindPeriodic(contractId, ClosingPeriodStart);
    }
}
