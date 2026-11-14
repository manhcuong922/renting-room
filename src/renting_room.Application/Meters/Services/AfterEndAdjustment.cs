using renting_room.Application.Billing;
using renting_room.Application.Fees;
using renting_room.Domain.Billing;
using renting_room.Domain.Common;
using renting_room.Domain.Contracts;
using renting_room.Domain.Meters;

namespace renting_room.Application.Meters;

/// <summary>
/// M06 MT-BR-19 "Sau thanh lý": HĐ đã kết thúc, số cuối lúc trả phòng gõ cao ⇒ sửa chuỗi chỉ số, hóa đơn trả phòng giữ nguyên, phần chênh
/// thành khoản "Chênh lệch sau thanh lý". Gọi trong transaction của người gọi — đã khóa HĐ → công tơ → hóa đơn trả phòng (C-07).
/// </summary>
public sealed class AfterEndAdjustment(
    ReadingAdjustmentCharges adjustmentCharges, IFeeQueries fees, IInvoiceRepository invoiceRepository, TimeProvider clock)
{
    public const string NotReturnedReason = "Không trả lại";

    /// <param name="settlement">"Đã trả lại" — khoản đánh dấu đã trả; cùng <paramref name="notReturned"/> = false ⇒ khoản chờ xử lý.</param>
    public async Task<Result<RoomCharge?>> ApplyAsync(
        Contract contract, Meter meter, Invoice source, MeterReading reading, decimal value, string reason, AdjustmentSettlement? settlement,
        bool notReturned, CancellationToken ct)
    {
        if (value > reading.Value)
            return MeterErrors.AdjustmentAfterEndIncrease;
        var oldValue = reading.Value;
        var line = source.LineOf(source.SegmentEndingAt(reading.Id)!)!;
        var corrected = meter.Correct(reading.Id, value, reason);
        if (corrected.IsFailure)
            return corrected.Error!;
        var change = source.PreviewReadingChange(reading.Id, value, await adjustmentCharges.FallbackPriceAsync(line, ct));
        if (change.IsFailure)
            return change.Error!;
        var existing = await adjustmentCharges.FindOpenAsync(source, line, ct);
        if (existing.IsFailure)
            return existing.Error!;

        var credit = Math.Min(change.Value!.Difference + source.WrittenOffAmount, 0);
        var fee = (await fees.FindTypeAsync(line.FeeTypeId!.Value, ct))!;
        var description = $"Chênh lệch sau thanh lý — {fee.Name} kỳ {line.ServiceFrom:dd/MM}–{line.ServiceTo:dd/MM}: " +
            $"{oldValue:0.##} → {value:0.##} (−{oldValue - value:0.##} {fee.Unit})";
        // M-05: số tiền luôn so với số lúc chốt hóa đơn ⇒ đã gồm phần trước đó chủ trọ chọn "Không trả lại" — nói rõ để không hoàn nhầm phần đã quyết bỏ
        // (không tự trừ: nhầm tay "Không trả lại" thì sửa lại số vẫn đòi lại được).
        if (await adjustmentCharges.WaivedAmountAsync(source, line, NotReturnedReason, ct) is var waived and > 0)
            description = Truncate($"{description} · gồm {waived:N0}đ từng chọn \"{NotReturnedReason}\"");
        var charge = await adjustmentCharges.SetAsync(contract, source, line, existing.Value, credit, description, reason, ct);
        if (charge.IsFailure)
            return charge.Error!;
        if (charge.Value is { } created)
        {
            var resolved = Resolve(created, settlement, notReturned);
            if (resolved.IsFailure)
                return resolved.Error!;
        }

        source.MarkAdjusted();
        invoiceRepository.AddAdjustment(InvoiceAdjustment.Record(source, reading.Id, change.Value!, oldValue, value, AdjustmentHandling.AfterEnd,
            charge.Value?.Id, [], reason));
        return charge.Value;
    }

    private static string Truncate(string text) => text.Length <= 200 ? text : text[..199] + "…"; // cột mô tả khoản tối đa 200 ký tự

    private Result Resolve(RoomCharge charge, AdjustmentSettlement? settlement, bool notReturned)
    {
        var now = clock.GetUtcNow();
        if (notReturned)
            return charge.Cancel(NotReturnedReason, now);
        return settlement is null ? Result.Success() : charge.Settle(settlement.Date, settlement.Method, now.ToBusinessDate());
    }
}
