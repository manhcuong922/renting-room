using renting_room.Domain.Contracts;
using renting_room.Domain.Meters;
using renting_room.Domain.Properties;

namespace renting_room.Domain.Billing;

public sealed record UsagePeriod(DateOnly Start, DateOnly End, DateOnly ClosingPeriodStart, bool EndsWithFinal)
{
    public static UsagePeriod? For(Contract contract, BillingPeriod period, BillingSchedule schedule)
    {
        var previous = contract.BillingPeriods(schedule, period.Start).LastOrDefault(p => p.End < period.Start);
        var previousMode = previous is null ? (ChargeMode?)null : schedule.ChargeModeOn(previous.Start);
        if (schedule.ChargeModeOn(period.Start) == ChargeMode.Postpaid)
        {
            var start = previousMode == ChargeMode.Prepaid ? previous!.Start : period.Start;
            return new UsagePeriod(start, period.End, period.Start, false);
        }

        return previous is null || previousMode == ChargeMode.Postpaid
            ? null
            : new UsagePeriod(previous.Start, previous.End, previous.Start, false);
    }

    public static IReadOnlyList<UsagePeriod> ForFinal(Contract contract, BillingPeriod lastPeriod, bool lastPeriodHasRegular, BillingSchedule schedule)
    {
        var last = new UsagePeriod(lastPeriod.Start, lastPeriod.End, lastPeriod.Start, true);
        return !lastPeriodHasRegular
            && contract.BillingPeriods(schedule, lastPeriod.Start).LastOrDefault(p => p.End < lastPeriod.Start) is { } previous
            && schedule.ChargeModeOn(previous.Start) == ChargeMode.Prepaid
                ? [new UsagePeriod(previous.Start, previous.End, previous.Start, false), last]
                : [last];
    }

    public MeterReading? StartReading(Meter meter, Guid? lastSegmentEndReadingId, Guid contractId, RoomStay? stay = null)
    {
        if (lastSegmentEndReadingId is { } id)
        {
            var chained = meter.Find(id);
            if (stay is null || chained is null || chained.ReadingDate >= stay.From)
                return chained;
        }
        var movedIn = stay is not null && stay.From > Start;
        if (meter.InstalledDate > (movedIn ? stay!.From : Start))
            return meter.Ordered.FirstOrDefault(r => r.Kind == ReadingKind.Initial);
        return movedIn ? meter.FindHandover(contractId, stay!.From, stay.To) : meter.LatestOnOrBefore(Start);
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
