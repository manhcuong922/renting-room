using FluentValidation;
using Mediator;
using Microsoft.EntityFrameworkCore;
using renting_room.Application.Common.Interfaces;
using renting_room.Application.Common.Validation;
using renting_room.Domain.Common;
using renting_room.Domain.Contracts;
using renting_room.Domain.Fees;
using renting_room.Domain.Meters;
using renting_room.Domain.Properties;

namespace renting_room.Application.Meters;

public sealed record MeterReadingDto(
    Guid Id, ReadingKind Kind, DateOnly ReadingDate, decimal Value, Guid? ContractId, string? Note, string Version);

/// <param name="LatestReading">Chỉ số mới nhất — UI dùng cho nút "Dùng số mới nhất" (MT-BR-13).</param>
public sealed record MeterDto(
    Guid Id,
    Guid RoomId,
    Guid FeeTypeId,
    string FeeTypeName,
    string Unit,
    string? SerialNo,
    DateOnly InstalledDate,
    DateOnly? RemovedDate,
    Guid? ReplacedByMeterId,
    bool IsActive,
    MeterReadingDto? LatestReading,
    string? Note,
    string Version);

public sealed record MeterRemovedDto(IReadOnlyList<Warning> Warnings);

internal static class MeterMapping
{
    public static MeterReadingDto ToDto(this MeterReading r) =>
        new(r.Id, r.Kind, r.ReadingDate, r.Value, r.ContractId, r.Note, r.Version.ToString());

    public static MeterDto ToDto(this Meter m, FeeType? fee) =>
        new(m.Id, m.RoomId, m.FeeTypeId, fee?.Name ?? string.Empty, fee?.Unit ?? string.Empty, m.SerialNo, m.InstalledDate, m.RemovedDate,
            m.ReplacedByMeterId, m.IsActive, m.Latest?.ToDto(), m.Note, m.Version.ToString());

    public static bool IsValidValue(decimal value) => value >= 0 && value <= Meter.MaxValue && decimal.Round(value, 2) == value;

    public static IRuleBuilderOptions<T, decimal> ReadingValue<T>(this IRuleBuilder<T, decimal> rule) =>
        rule.Must(IsValidValue).WithErrorCode("INVALID_READING").WithMessage("Chỉ số 0–99.999.999,99, tối đa 2 số lẻ.");

    public static IRuleBuilderOptions<T, DateOnly> ReadingDate<T>(this IRuleBuilder<T, DateOnly> rule, TimeProvider clock) =>
        rule.Must(d => d <= clock.GetUtcNow().ToBusinessDate().AddDays(1))
            .WithErrorCode("INVALID_READING_DATE").WithMessage("Ngày ghi chỉ số không được quá hôm nay + 1.");

    /// <summary>Khóa công tơ (sắp theo id — tránh deadlock C-07) rồi nạp kèm chỉ số.</summary>
    public static async Task<List<Meter>> LockAndLoadAsync(IAppDbContext db, IEnumerable<Guid> meterIds, CancellationToken ct)
    {
        var ids = meterIds.Distinct().OrderBy(id => id).ToList();
        foreach (var id in ids)
            await db.LockForUpdateAsync<Meter>(id, ct);
        return await db.Meters.Include(m => m.Readings).Where(m => ids.Contains(m.Id)).ToListAsync(ct);
    }
}

// ============================================================ Truy vấn

public sealed record ListRoomMetersQuery(Guid RoomId, bool IncludeRemoved) : IRequest<Result<IReadOnlyList<MeterDto>>>;

public sealed class ListRoomMetersHandler(IAppDbContext db) : IRequestHandler<ListRoomMetersQuery, Result<IReadOnlyList<MeterDto>>>
{
    public async ValueTask<Result<IReadOnlyList<MeterDto>>> Handle(ListRoomMetersQuery request, CancellationToken cancellationToken)
    {
        if (!await db.Rooms.AnyAsync(r => r.Id == request.RoomId, cancellationToken))
            return PropertyErrors.RoomNotFound;

        var meters = await db.Meters.AsNoTracking().Include(m => m.Readings)
            .Where(m => m.RoomId == request.RoomId && (request.IncludeRemoved || m.RemovedDate == null))
            .ToListAsync(cancellationToken);
        var feeIds = meters.Select(m => m.FeeTypeId).Distinct().ToList();
        var fees = await db.FeeTypes.AsNoTracking().Where(f => feeIds.Contains(f.Id)).ToDictionaryAsync(f => f.Id, cancellationToken);

        return meters
            .OrderBy(m => fees.GetValueOrDefault(m.FeeTypeId)?.SortOrder).ThenByDescending(m => m.InstalledDate)
            .Select(m => m.ToDto(fees.GetValueOrDefault(m.FeeTypeId)))
            .ToList();
    }
}

public sealed record ListMeterReadingsQuery(Guid MeterId) : IRequest<Result<IReadOnlyList<MeterReadingDto>>>;

public sealed class ListMeterReadingsHandler(IAppDbContext db) : IRequestHandler<ListMeterReadingsQuery, Result<IReadOnlyList<MeterReadingDto>>>
{
    public async ValueTask<Result<IReadOnlyList<MeterReadingDto>>> Handle(ListMeterReadingsQuery request, CancellationToken cancellationToken)
    {
        var meter = await db.Meters.AsNoTracking().Include(m => m.Readings).FirstOrDefaultAsync(m => m.Id == request.MeterId, cancellationToken);
        if (meter is null)
            return MeterErrors.NotFound;
        return meter.Ordered.Reverse().Select(r => r.ToDto()).ToList();
    }
}

// ============================================================ Lắp / thay / tháo

public sealed record InstallMeterCommand(
    Guid RoomId, Guid FeeTypeId, string? SerialNo, DateOnly InstalledDate, decimal InitialValue, string? Note) : IRequest<Result<Guid>>;

public sealed class InstallMeterCommandValidator : AbstractValidator<InstallMeterCommand>
{
    public InstallMeterCommandValidator(TimeProvider clock)
    {
        RuleFor(x => x.FeeTypeId).NotEmpty().WithErrorCode("REQUIRED");
        RuleFor(x => x.SerialNo).OptionalText(50);
        RuleFor(x => x.InstalledDate).ReadingDate(clock);
        RuleFor(x => x.InitialValue).ReadingValue();
        RuleFor(x => x.Note).OptionalText(300);
    }
}

/// <summary>MT-UC-01: lắp công tơ cho phòng — khoản thu phải là điện nước theo công tơ, cùng khu, chưa ngừng dùng (MT-BR-01/02).</summary>
public sealed class InstallMeterHandler(IAppDbContext db) : IRequestHandler<InstallMeterCommand, Result<Guid>>
{
    public async ValueTask<Result<Guid>> Handle(InstallMeterCommand request, CancellationToken cancellationToken)
    {
        var room = await db.Rooms.AsNoTracking().FirstOrDefaultAsync(r => r.Id == request.RoomId, cancellationToken);
        if (room is null)
            return PropertyErrors.RoomNotFound;
        if (room.IsArchived)
            return PropertyErrors.RoomArchived;

        var fee = await db.FeeTypes.AsNoTracking().FirstOrDefaultAsync(f => f.Id == request.FeeTypeId, cancellationToken);
        if (fee is null || fee.PropertyId != room.PropertyId)
            return FeeErrors.NotInProperty;
        if (fee.Group != FeeGroup.Metered)
            return MeterErrors.FeeNotMetered;
        if (fee.IsArchived)
            return FeeErrors.Archived;
        if (await db.Meters.AnyAsync(m => m.RoomId == room.Id && m.FeeTypeId == fee.Id && m.RemovedDate == null, cancellationToken))
            return MeterErrors.AlreadyActive;

        var meter = Meter.Install(room.PropertyId, room.Id, fee.Id, request.SerialNo, request.InstalledDate, request.InitialValue, request.Note);
        db.Meters.Add(meter);
        await db.SaveChangesAsync(cancellationToken); // unique "1 công tơ hoạt động" là chốt chặn khi 2 request song song
        return meter.Id;
    }
}

public sealed record ReplaceMeterCommand(
    Guid MeterId, DateOnly Date, decimal OldFinalValue, string? NewSerialNo, decimal NewInitialValue, string? Note) : IRequest<Result<Guid>>;

public sealed class ReplaceMeterCommandValidator : AbstractValidator<ReplaceMeterCommand>
{
    public ReplaceMeterCommandValidator(TimeProvider clock)
    {
        RuleFor(x => x.Date).ReadingDate(clock);
        RuleFor(x => x.OldFinalValue).ReadingValue();
        RuleFor(x => x.NewInitialValue).ReadingValue();
        RuleFor(x => x.NewSerialNo).OptionalText(50);
        RuleFor(x => x.Note).OptionalText(300);
    }
}

/// <summary>MT-UC-02 / MT-BR-09: thay công tơ (phiên bản) — số cuối công tơ cũ + số ban đầu công tơ mới, 1 transaction.</summary>
public sealed class ReplaceMeterHandler(IAppDbContext db) : IRequestHandler<ReplaceMeterCommand, Result<Guid>>
{
    public async ValueTask<Result<Guid>> Handle(ReplaceMeterCommand request, CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var old = (await MeterMapping.LockAndLoadAsync(db, [request.MeterId], cancellationToken)).FirstOrDefault();
        if (old is null)
            return MeterErrors.NotFound;

        var next = old.ReplaceWith(request.Date, request.OldFinalValue, request.NewSerialNo, request.NewInitialValue, request.Note);
        if (next.IsFailure)
            return next.Error!;

        // Lưu 2 lần: tháo bản cũ trước rồi mới lắp bản mới — unique "1 công tơ hoạt động" kiểm từng câu lệnh.
        await db.SaveChangesAsync(cancellationToken);
        db.Meters.Add(next.Value!);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return next.Value!.Id;
    }
}

public sealed record RemoveMeterCommand(Guid MeterId, DateOnly Date, decimal FinalValue, string? Note) : IRequest<Result<MeterRemovedDto>>;

public sealed class RemoveMeterCommandValidator : AbstractValidator<RemoveMeterCommand>
{
    public RemoveMeterCommandValidator(TimeProvider clock)
    {
        RuleFor(x => x.Date).ReadingDate(clock);
        RuleFor(x => x.FinalValue).ReadingValue();
        RuleFor(x => x.Note).OptionalText(300);
    }
}

/// <summary>MT-UC-03 / MT-BR-10: gỡ công tơ (không thay) — bắt buộc số cuối; phòng đang có HĐ ⇒ cảnh báo.</summary>
public sealed class RemoveMeterHandler(IAppDbContext db) : IRequestHandler<RemoveMeterCommand, Result<MeterRemovedDto>>
{
    public async ValueTask<Result<MeterRemovedDto>> Handle(RemoveMeterCommand request, CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var meter = (await MeterMapping.LockAndLoadAsync(db, [request.MeterId], cancellationToken)).FirstOrDefault();
        if (meter is null)
            return MeterErrors.NotFound;

        var removed = meter.Remove(request.Date, request.FinalValue, request.Note);
        if (removed.IsFailure)
            return removed.Error!;

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        var hasContract = await db.Contracts.AnyAsync(c => c.RoomId == meter.RoomId
            && (c.Status == ContractStatus.Active || c.Status == ContractStatus.Liquidating), cancellationToken);
        return new MeterRemovedDto(hasContract
            ? [new Warning("ROOM_HAS_OPEN_CONTRACT", "Phòng đang có người thuê — từ kỳ sau phiếu không còn dòng của khoản này.")]
            : []);
    }
}

// ============================================================ Sửa chỉ số

public sealed record CorrectMeterReadingCommand(Guid ReadingId, decimal Value, string? Note) : IRequest<Result<MeterReadingDto>>;

public sealed class CorrectMeterReadingCommandValidator : AbstractValidator<CorrectMeterReadingCommand>
{
    public CorrectMeterReadingCommandValidator()
    {
        RuleFor(x => x.Value).ReadingValue();
        RuleFor(x => x.Note).OptionalText(300);
    }
}

/// <summary>MT-UC-06: sửa chỉ số nhập sai (VD chỉnh chỉ số nhận phòng 100 → 108) — vẫn đơn điệu; đã dùng cho phiếu đã chốt ⇒ khóa (MT-BR-06).</summary>
public sealed class CorrectMeterReadingHandler(IAppDbContext db) : IRequestHandler<CorrectMeterReadingCommand, Result<MeterReadingDto>>
{
    public async ValueTask<Result<MeterReadingDto>> Handle(CorrectMeterReadingCommand request, CancellationToken cancellationToken)
    {
        var meterId = await db.Meters.SelectMany(m => m.Readings).Where(r => r.Id == request.ReadingId)
            .Select(r => (Guid?)r.MeterId).FirstOrDefaultAsync(cancellationToken);
        if (meterId is null)
            return MeterErrors.ReadingNotFound;

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var meter = (await MeterMapping.LockAndLoadAsync(db, [meterId.Value], cancellationToken)).Single();
        if ((await ReadingLocks.LockedReadingIdsAsync(db, [meter.Id], cancellationToken)).Contains(request.ReadingId))
            return MeterErrors.ReadingLocked;
        var corrected = meter.Correct(request.ReadingId, request.Value, request.Note);
        if (corrected.IsFailure)
            return corrected.Error!;

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return corrected.Value!.ToDto();
    }
}

// ============================================================ Dùng từ M05: chỉ số nhận phòng / chỉ số cuối

/// <param name="Value">null = "Dùng số mới nhất" (chỉ số gần nhất ≤ ngày) — chỉ cho chỉ số nhận phòng.</param>
public sealed record MeterReadingInput(Guid MeterId, decimal? Value);

internal static class ContractMeterReadings
{
    private static Task<List<Guid>> ActiveMeterIdsAsync(IAppDbContext db, Guid roomId, DateOnly date, CancellationToken ct) =>
        db.Meters.Where(m => m.RoomId == roomId && m.InstalledDate <= date && (m.RemovedDate == null || m.RemovedDate > date))
            .Select(m => m.Id).ToListAsync(ct);

    /// <summary>
    /// Ghi chỉ số <paramref name="kind"/> tại <paramref name="date"/> cho <b>mọi</b> công tơ của phòng đang đo tại ngày đó.
    /// Nhận phòng (MT-BR-13): mỗi công tơ phải có dòng, value null = số mới nhất. Chỉ số cuối (MT-UC-05, lập phiếu quyết toán):
    /// đã có thì value null = giữ, có số = sửa; chưa có thì bắt buộc nhập số. Thiếu ⇒ lỗi kèm danh sách công tơ; công tơ lạ ⇒ lỗi.
    /// </summary>
    public static async Task<Result> RecordAsync(
        IAppDbContext db, Contract contract, ReadingKind kind, DateOnly date, IReadOnlyList<MeterReadingInput>? inputs, CancellationToken ct)
    {
        var roomMeterIds = await ActiveMeterIdsAsync(db, contract.RoomId, date, ct);
        var given = (inputs ?? []).ToDictionary(i => i.MeterId);
        if (given.Keys.Any(id => !roomMeterIds.Contains(id)))
            return Result.Failure(MeterErrors.UnknownMeter);
        if (roomMeterIds.Count == 0)
            return Result.Success();

        var meters = await MeterMapping.LockAndLoadAsync(db, roomMeterIds, ct);
        var missing = meters.Where(m => kind == ReadingKind.Final
                ? (!given.TryGetValue(m.Id, out var f) || f.Value is null) && m.FindFinal(contract.Id) is null
                : !given.ContainsKey(m.Id))
            .Select(m => m.Id).ToList();
        if (missing.Count > 0)
            return Result.Failure(kind == ReadingKind.Final ? MeterErrors.FinalReadingRequired(missing) : MeterErrors.HandoverReadingRequired(missing));

        foreach (var meter in meters)
        {
            var input = given.GetValueOrDefault(meter.Id);
            var existingFinal = kind == ReadingKind.Final ? meter.FindFinal(contract.Id) : null;
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
    public static async Task<IReadOnlyList<Guid>> MissingFinalAsync(IAppDbContext db, Contract contract, DateOnly date, CancellationToken ct)
    {
        var ids = await ActiveMeterIdsAsync(db, contract.RoomId, date, ct);
        var meters = await db.Meters.AsNoTracking().Include(m => m.Readings).Where(m => ids.Contains(m.Id)).ToListAsync(ct);
        return meters.Where(m => m.FindFinal(contract.Id) is null).Select(m => m.Id).ToList();
    }

    /// <summary>Hủy thanh lý ⇒ hủy chỉ số cuối đã ghi của HĐ (chưa bị khóa vì phiếu quyết toán chưa chốt).</summary>
    public static async Task VoidFinalAsync(IAppDbContext db, Contract contract, DateTimeOffset now, CancellationToken ct)
    {
        var ids = await db.Meters.Where(m => m.RoomId == contract.RoomId).Select(m => m.Id).ToListAsync(ct);
        foreach (var meter in await MeterMapping.LockAndLoadAsync(db, ids, ct))
            if (meter.FindFinal(contract.Id) is { } final)
                meter.VoidReading(final.Id, "Hủy thanh lý", now);
    }
}
