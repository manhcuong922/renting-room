using FluentValidation;
using renting_room.Domain.Common;
using renting_room.Domain.Contracts;
using renting_room.Domain.Meters;

namespace renting_room.Application.Meters;

/// <param name="Value">null = "Dùng số mới nhất" (chỉ số gần nhất ≤ ngày) — chỉ cho chỉ số nhận phòng.</param>
public sealed record MeterReadingInput(Guid MeterId, decimal? Value);

public sealed class ContractMeterReadings(IMeterRepository meters, IMeterQueries meterQueries)
{
    /// <summary>
    /// Ghi chỉ số <paramref name="kind"/> tại <paramref name="date"/> cho <b>mọi</b> công tơ của phòng đang đo tại ngày đó.
    /// Nhận phòng (MT-BR-13): mỗi công tơ phải có dòng, value null = số mới nhất. Chỉ số cuối (MT-UC-05, lập phiếu quyết toán):
    /// đã có thì value null = giữ, có số = sửa; chưa có thì bắt buộc nhập số. Thiếu ⇒ lỗi kèm danh sách công tơ; công tơ lạ ⇒ lỗi.
    /// </summary>
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

    /// <summary>CT-BR-12: công tơ đang đo tại ngày trả phòng mà chưa có chỉ số cuối của HĐ.</summary>
    public async Task<IReadOnlyList<Guid>> MissingFinalAsync(Contract contract, DateOnly date, CancellationToken ct)
    {
        var ids = await meterQueries.ListActiveIdsInRoomAsync(contract.RoomId, date, ct);
        var active = await meterQueries.ListAsync(ids, ct);
        return active.Where(m => m.FindFinal(contract.Id, contract.RoomSince) is null).Select(m => m.Id).ToList();
    }

    /// <summary>Hủy thanh lý ⇒ hủy chỉ số cuối đã ghi của HĐ (chưa bị khóa vì phiếu quyết toán chưa chốt).</summary>
    public async Task VoidFinalAsync(Contract contract, DateTimeOffset now, CancellationToken ct)
    {
        var ids = await meterQueries.ListIdsInRoomAsync(contract.RoomId, ct);
        foreach (var meter in await meters.LockAndLoadAsync(ids, ct))
            if (meter.FindFinal(contract.Id, contract.RoomSince) is { } final)
                meter.VoidReading(final.Id, "Hủy thanh lý", now);
    }
}
