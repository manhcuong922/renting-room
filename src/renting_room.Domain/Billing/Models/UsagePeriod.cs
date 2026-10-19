using renting_room.Domain.Contracts;
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
    /// Phiếu quyết toán: điện nước tới chỉ số cuối HĐ, bắt đầu từ đầu kỳ cuối. Prepaid mà kỳ cuối chưa có phiếu thường ⇒ kỳ trước chưa ai thu:
    /// thêm kỳ trước thành <b>kỳ sử dụng riêng</b> (tới chỉ số cuối kỳ) — mỗi kỳ một dòng, giá / bậc tính riêng (BL-BR-05, 17/10/2026).
    /// Chỉ số đầu vẫn nối chuỗi từ đoạn đo đã lập phiếu (MT-BR-12) nên không thu trùng.
    /// </summary>
    public static IReadOnlyList<UsagePeriod> ForFinal(Contract contract, BillingPeriod lastPeriod, bool lastPeriodHasRegular, BillingSchedule schedule)
    {
        var last = new UsagePeriod(lastPeriod.Start, lastPeriod.End, lastPeriod.Start, true);
        return !lastPeriodHasRegular
            && contract.BillingPeriods(schedule, lastPeriod.Start).LastOrDefault(p => p.End < lastPeriod.Start) is { } previous
            && schedule.ChargeModeOn(previous.Start) == ChargeMode.Prepaid
                ? [new UsagePeriod(previous.Start, previous.End, previous.Start, false), last]
                : [last];
    }

    /// <summary>
    /// M06 §3.3 — chỉ số đầu: số cuối của đoạn đo gần nhất trên phiếu chưa hủy của HĐ (MT-BR-12); chưa có phiếu nào ⇒ chỉ số lắp
    /// (công tơ lắp trong kỳ) hoặc chỉ số gần nhất ≤ đầu kỳ (nhận phòng / tháng trước — phiếu đầu tiên của HĐ nhập từ trước).
    /// </summary>
    /// <param name="stay">Lần HĐ ở phòng của công tơ: vào phòng giữa kỳ ⇒ bắt đầu từ chỉ số nhận phòng của lần ở; số cuối của đoạn đo trước
    /// chỉ nối tiếp khi ghi trong lần ở này — HĐ rời phòng rồi quay lại không bị tính phần phòng trống (MT-BR-12).</param>
    public MeterReading? StartReading(Meter meter, Guid? lastSegmentEndReadingId, Guid contractId, RoomStay? stay = null)
    {
        if (lastSegmentEndReadingId is { } id)
        {
            var chained = meter.Find(id);
            if (stay is null || chained is null || chained.ReadingDate >= stay.From)
                return chained;
        }
        var movedIn = stay is not null && stay.From > Start;
        // Công tơ lắp / thay sau mốc bắt đầu (đầu kỳ hoặc ngày vào phòng) ⇒ số ban đầu của công tơ.
        if (meter.InstalledDate > (movedIn ? stay!.From : Start))
            return meter.Ordered.FirstOrDefault(r => r.Kind == ReadingKind.Initial);
        return movedIn ? meter.FindHandover(contractId, stay!.From, stay.To) : meter.LatestOnOrBefore(Start);
    }

    /// <summary>
    /// Chỉ số cuối: tháo công tơ trong kỳ (khi HĐ còn ở) ⇒ số tháo (MT-BR-15). Lần ở <paramref name="left"/> đã rời (chuyển phòng — CT-BR-47):
    /// ngày chuyển trong kỳ ⇒ chỉ số cuối lúc chuyển; chuyển sau kỳ ⇒ chỉ số cuối kỳ, chưa ghi thì chỉ số lúc chuyển (vài ngày sau kỳ tính luôn
    /// vào kỳ này, kỳ sau không tính lại). Kỳ cuối ⇒ chỉ số cuối HĐ của lần ở hiện tại; còn lại ⇒ chỉ số cuối kỳ.
    /// </summary>
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
