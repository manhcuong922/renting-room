using FluentValidation;
using renting_room.Application.Contracts;
using renting_room.Domain.Common;
using renting_room.Domain.Contracts;
using renting_room.Domain.Meters;

namespace renting_room.Application.Meters;

/// <param name="Value">null = "Dùng số mới nhất" (chỉ số gần nhất ≤ ngày) — chỉ cho chỉ số nhận phòng.</param>
/// <param name="AcceptBelowPrevious">Chỉ số nhận phòng nhỏ hơn số của HĐ trước: chủ trọ chấp nhận ⇒ ghi bình thường, HĐ cũ giữ nguyên (M05 CT-BR-02, M-10).</param>
public sealed record MeterReadingInput(Guid MeterId, decimal? Value, bool AcceptBelowPrevious = false);

public sealed class ContractMeterReadings(IMeterRepository meters, IMeterQueries meterQueries, IContractQueries contractQueries)
{
    public async Task<Result> RecordAsync(
        Contract contract, ReadingKind kind, DateOnly date, IReadOnlyList<MeterReadingInput>? inputs, CancellationToken ct)
    {
        var roomMeterIds = await meterQueries.ListActiveIdsInRoomAsync(contract.RoomId, date, ct);
        var given = (inputs ?? []).ToDictionary(i => i.MeterId);
        if (given.Keys.Any(id => !roomMeterIds.Contains(id)))
            return Result.Failure(MeterErrors.UnknownMeter);
        if (roomMeterIds.Count == 0)
            return Result.Success();

        var loaded = await meters.LockAndLoadAsync(roomMeterIds, ct);
        var missing = loaded.Where(m => kind == ReadingKind.Final
                ? (!given.TryGetValue(m.Id, out var f) || f.Value is null) && m.FindFinal(contract.Id, contract.RoomSince) is null
                : !given.ContainsKey(m.Id))
            .Select(m => m.Id).ToList();
        if (missing.Count > 0)
            return Result.Failure(kind == ReadingKind.Final ? MeterErrors.FinalReadingRequired(missing) : MeterErrors.HandoverReadingRequired(missing));

        foreach (var meter in loaded)
        {
            var input = given.GetValueOrDefault(meter.Id);
            if (kind == ReadingKind.Handover && await BelowPreviousContractAsync(meter, contract, date, input, ct) is { } warning)
                return Result.Failure(warning);
            var existingFinal = kind == ReadingKind.Final ? meter.FindFinal(contract.Id, contract.RoomSince) : null;
            if (existingFinal is not null)
            {
                if (input?.Value is { } newValue && newValue != existingFinal.Value)
                {
                    var corrected = meter.Correct(existingFinal.Id, newValue, null);
                    if (corrected.IsFailure)
                        return Result.Failure(corrected.Error!.WithDetail("meterId", meter.Id));
                }
                continue;
            }

            var value = input?.Value ?? meter.LatestOnOrBefore(date)?.Value ?? 0;
            var recorded = meter.Record(kind, date, value, contract.Id, note: null);
            if (recorded.IsFailure)
                return Result.Failure(recorded.Error!.WithDetail("meterId", meter.Id));
        }
        return Result.Success();
    }

    /// <summary>
    /// M-10 (M05 CT-BR-02): số nhận phòng nhỏ hơn chỉ số gần nhất của HĐ khác (đang thanh lý / đã kết thúc) ⇒ cảnh báo, chủ trọ chấp nhận mới ghi.
    /// Mỗi HĐ có dải số riêng nên HĐ cũ không bị đụng tới.
    /// </summary>
    private async Task<Error?> BelowPreviousContractAsync(Meter meter, Contract contract, DateOnly date, MeterReadingInput? input, CancellationToken ct)
    {
        if (input is not { Value: { } value, AcceptBelowPrevious: false }
            || meter.LatestOnOrBefore(date) is not { ContractId: { } previousId } latest || previousId == contract.Id || value >= latest.Value)
            return null;
        var previous = (await contractQueries.GetAsync(previousId, ct))!;
        return MeterErrors.HandoverBelowPreviousFinal(meter.Id, latest.Value, previous.ContractNo);
    }

    public async Task<IReadOnlyList<Guid>> MissingFinalAsync(Contract contract, DateOnly date, CancellationToken ct)
    {
        var ids = await meterQueries.ListActiveIdsInRoomAsync(contract.RoomId, date, ct);
        var active = await meterQueries.ListAsync(ids, ct);
        return active.Where(m => m.FindFinal(contract.Id, contract.RoomSince) is null).Select(m => m.Id).ToList();
    }

    /// <summary>
    /// H-02 (M05 CT-BR-11): chỉ số cuối bắt buộc khi thanh lý — trừ ngày trả phòng trước "Tính tiền từ ngày" (chưa có chỉ số nhận phòng để đối chiếu).
    /// Ngày trả phòng tương lai: nhập sau, trước hoàn tất.
    /// </summary>
    public static bool FinalRequiredAt(Contract contract, DateOnly endDate) => endDate >= contract.BillingStartDate;

    /// <summary>
    /// Người mới nhận phòng khi HĐ trước của phòng đang thanh lý mà chưa có chỉ số cuối ⇒ "Dùng số mới nhất" sẽ là số chốt kỳ cũ (H-02, M05 CT-BR-02).
    /// <paramref name="exceptContractId"/>: HĐ cũ của lần ký lại (số cuối tự ghi theo số nhận phòng — CT-UC-21).
    /// </summary>
    public async Task<Error?> FindPreviousFinalMissingAsync(Guid roomId, DateOnly date, Guid? exceptContractId, CancellationToken ct)
    {
        var previous = (await contractQueries.ListLivedInRoomAsync(roomId, ct))
            .Where(c => c.Status == ContractStatus.Liquidating && c.RoomId == roomId && c.Id != exceptContractId
                && c.ActualEndDate is { } end && end < date && FinalRequiredAt(c, end))
            .OrderByDescending(c => c.ActualEndDate).FirstOrDefault();
        if (previous is null)
            return null;
        var missing = await MissingFinalAsync(previous, previous.ActualEndDate!.Value, ct);
        return missing.Count == 0 ? null : MeterErrors.PreviousFinalReadingRequired(previous.ContractNo, missing);
    }

    /// <summary>Ký lại (CT-UC-21): số cuối HĐ cũ tại ngày bàn giao = số nhận phòng HĐ mới — cùng người, liền nhau. Gọi sau khi đã ghi chỉ số nhận phòng.</summary>
    public async Task<Result> RecordFinalFromHandoverAsync(Contract old, Contract next, CancellationToken ct)
    {
        var date = old.ActualEndDate!.Value;
        if (!FinalRequiredAt(old, date))
            return Result.Success();
        var ids = await meterQueries.ListActiveIdsInRoomAsync(next.RoomId, date, ct);
        // Công tơ thay đúng ngày bàn giao không có chỉ số nhận phòng của HĐ mới ⇒ giữ chỉ số gần nhất tới ngày đó (không dùng thêm).
        var inputs = (await meters.LockAndLoadAsync(ids, ct))
            .Select(m => (m.Id, Value: m.FindHandover(next.Id)?.Value ?? m.LatestOnOrBefore(date)?.Value,
                OldLast: m.Ordered.LastOrDefault(r => r.ContractId == old.Id && r.Kind != ReadingKind.Final && r.ReadingDate <= date)?.Value))
            .Where(x => x.Value is not null)
            // Số nhận phòng nhỏ hơn số chốt kỳ cuối của HĐ cũ (M-10): số cuối HĐ cũ không được thấp hơn số trong chính HĐ đó ⇒ giữ số chốt kỳ.
            .Select(x => new MeterReadingInput(x.Id, Math.Max(x.Value!.Value, x.OldLast ?? 0)))
            .ToList();
        return await RecordAsync(old, ReadingKind.Final, date, inputs, ct);
    }

    /// <summary>Hủy thanh lý ⇒ hủy chỉ số cuối đã ghi của HĐ (chưa bị khóa vì hóa đơn trả phòng chưa chốt).</summary>
    public async Task VoidFinalAsync(Contract contract, DateTimeOffset now, CancellationToken ct)
    {
        var ids = await meterQueries.ListIdsInRoomAsync(contract.RoomId, ct);
        foreach (var meter in await meters.LockAndLoadAsync(ids, ct))
            if (meter.FindFinal(contract.Id, contract.RoomSince) is { } final)
                meter.VoidReading(final.Id, "Hủy thanh lý", now);
    }
}
