using renting_room.Domain.Common;

namespace renting_room.Domain.Meters;

/// <summary>Loại chỉ số (M06). Cùng ngày xếp theo thứ tự khai báo: lắp → nhận phòng → định kỳ → cuối HĐ → tháo.</summary>
public enum ReadingKind
{
    Initial,   // lắp công tơ
    Handover,  // nhận phòng — mốc bắt đầu tính của HĐ (MT-BR-13)
    Periodic,  // cuối kỳ (đợt 2, cùng M07)
    Adhoc,     // kiểm tra, không dùng tính tiền
    Final,     // cuối HĐ (thanh lý)
    Removal    // tháo công tơ
}

/// <summary>
/// Công tơ của 1 phòng cho 1 khoản thu theo chỉ số (MT-BR-01/02). Có phiên bản: thay công tơ = tháo bản cũ (bắt buộc số cuối)
/// + lắp bản mới cùng ngày (MT-BR-09). Chỉ số nằm trong aggregate để kiểm đơn điệu ngay trong domain (MT-BR-03).
/// </summary>
public sealed class Meter : TenantEntity
{
    public const decimal MaxValue = 99_999_999.99m;

    private readonly List<MeterReading> _readings = [];

    private Meter() { } // EF Core

    public Guid PropertyId { get; private set; }
    public Guid RoomId { get; private set; }
    public Guid FeeTypeId { get; private set; }
    public string? SerialNo { get; private set; }
    public DateOnly InstalledDate { get; private set; }
    public DateOnly? RemovedDate { get; private set; }

    /// <summary>Công tơ thay thế bản này (phiên bản kế tiếp).</summary>
    public Guid? ReplacedByMeterId { get; private set; }
    public string? Note { get; private set; }

    public IReadOnlyList<MeterReading> Readings => _readings;
    public bool IsActive => RemovedDate is null;

    /// <summary>Đang đo tại ngày <paramref name="date"/>: ngày tháo thuộc về công tơ mới (lắp cùng ngày).</summary>
    public bool IsActiveOn(DateOnly date) => InstalledDate <= date && (RemovedDate is null || RemovedDate > date);

    public static Meter Install(
        Guid propertyId, Guid roomId, Guid feeTypeId, string? serialNo, DateOnly installedDate, decimal initialValue, string? note)
    {
        if (propertyId == Guid.Empty || roomId == Guid.Empty || feeTypeId == Guid.Empty)
            throw new ArgumentException("Property, room and fee type are required.");
        GuardValue(initialValue);

        var meter = new Meter
        {
            Id = Guid.CreateVersion7(),
            PropertyId = propertyId,
            RoomId = roomId,
            FeeTypeId = feeTypeId,
            SerialNo = TextNormalizer.TrimToNull(serialNo),
            InstalledDate = installedDate,
            Note = TextNormalizer.TrimToNull(note)
        };
        meter._readings.Add(new MeterReading(meter.Id, ReadingKind.Initial, installedDate, initialValue, null, null));
        return meter;
    }

    /// <summary>Chỉ số còn hiệu lực theo thứ tự thời gian (MT-BR-03).</summary>
    public IEnumerable<MeterReading> Ordered =>
        _readings.Where(r => r.VoidedAt is null).OrderBy(r => r.ReadingDate).ThenBy(r => r.Kind).ThenBy(SequenceKey);

    /// <summary>"Dùng số mới nhất" (MT-BR-13): chỉ số gần nhất có ngày ≤ <paramref name="date"/>.</summary>
    public MeterReading? LatestOnOrBefore(DateOnly date) => Ordered.LastOrDefault(r => r.ReadingDate <= date);

    public MeterReading? Latest => Ordered.LastOrDefault();

    public MeterReading? Find(Guid readingId) => _readings.FirstOrDefault(r => r.Id == readingId && r.VoidedAt is null);

    /// <summary>Chỉ số cuối kỳ của HĐ cho kỳ sử dụng bắt đầu <paramref name="closingPeriodStart"/> (MT-BR-04).</summary>
    public MeterReading? FindPeriodic(Guid contractId, DateOnly closingPeriodStart) =>
        Ordered.FirstOrDefault(r => r.Kind == ReadingKind.Periodic && r.ContractId == contractId && r.ClosingPeriodStart == closingPeriodStart);

    public MeterReading? FindFinal(Guid contractId) =>
        Ordered.FirstOrDefault(r => r.Kind == ReadingKind.Final && r.ContractId == contractId);

    /// <summary>Chỉ số nhận phòng của HĐ trên công tơ này (MT-BR-13; chuyển tới phòng giữa kỳ — CT-BR-47).</summary>
    public MeterReading? FindHandover(Guid contractId) =>
        Ordered.LastOrDefault(r => r.Kind == ReadingKind.Handover && r.ContractId == contractId);

    public MeterReading? Removal => Ordered.FirstOrDefault(r => r.Kind == ReadingKind.Removal);

    /// <summary>Công tơ đo trong (một phần) khoảng [<paramref name="from"/>, <paramref name="to"/>] — ngày tháo vẫn thuộc khoảng (chỉ số tháo là số cuối).</summary>
    public bool Overlaps(DateOnly from, DateOnly to) => InstalledDate <= to && (RemovedDate is null || RemovedDate >= from);

    /// <summary>Ghi chỉ số nhận phòng / cuối HĐ / kiểm tra. Lắp và tháo đi qua <see cref="Install"/> / <see cref="Remove"/>.</summary>
    public Result<MeterReading> Record(
        ReadingKind kind, DateOnly date, decimal value, Guid? contractId, string? note, DateOnly? closingPeriodStart = null)
    {
        if ((kind == ReadingKind.Periodic) != (closingPeriodStart is not null))
            throw new ArgumentException("Closing period start is required for periodic readings only.", nameof(closingPeriodStart));
        if (kind is ReadingKind.Initial or ReadingKind.Removal)
            throw new ArgumentException("Use Install / Remove for initial and removal readings.", nameof(kind));
        if (kind is ReadingKind.Handover or ReadingKind.Final or ReadingKind.Periodic && contractId is null)
            throw new ArgumentException("Contract is required for this reading kind.", nameof(contractId));
        GuardValue(value);
        if (date < InstalledDate || (RemovedDate is { } removed && date >= removed))
            return Result.Failure<MeterReading>(MeterErrors.InvalidReadingDate);

        var monotonic = CheckMonotonic(date, kind, long.MaxValue, value, exclude: null);
        if (monotonic.IsFailure)
            return Result.Failure<MeterReading>(monotonic.Error!);

        var reading = new MeterReading(Id, kind, date, value, contractId, note) { ClosingPeriodStart = closingPeriodStart };
        _readings.Add(reading);
        return reading;
    }

    /// <summary>MT-BR-10: tháo công tơ — bắt buộc số cuối; không được trước chỉ số đã ghi.</summary>
    public Result Remove(DateOnly date, decimal finalValue, string? note)
    {
        if (!IsActive)
            return Result.Failure(MeterErrors.Removed);
        GuardValue(finalValue);
        if (date < InstalledDate || Ordered.Any(r => r.ReadingDate > date))
            return Result.Failure(MeterErrors.InvalidReadingDate);

        var monotonic = CheckMonotonic(date, ReadingKind.Removal, long.MaxValue, finalValue, exclude: null);
        if (monotonic.IsFailure)
            return monotonic;

        _readings.Add(new MeterReading(Id, ReadingKind.Removal, date, finalValue, null, note));
        RemovedDate = date;
        return Result.Success();
    }

    /// <summary>MT-BR-09: thay công tơ = tháo bản cũ (số cuối) + lắp bản mới cùng ngày (số ban đầu).</summary>
    public Result<Meter> ReplaceWith(DateOnly date, decimal oldFinalValue, string? newSerialNo, decimal newInitialValue, string? note)
    {
        var removed = Remove(date, oldFinalValue, note);
        if (removed.IsFailure)
            return Result.Failure<Meter>(removed.Error!);

        var next = Install(PropertyId, RoomId, FeeTypeId, newSerialNo, date, newInitialValue, note);
        ReplacedByMeterId = next.Id;
        return next;
    }

    /// <summary>MT-UC-06: sửa giá trị chỉ số (nhập sai) — vẫn phải đơn điệu với chỉ số trước / sau.</summary>
    public Result<MeterReading> Correct(Guid readingId, decimal value, string? note)
    {
        var reading = _readings.FirstOrDefault(r => r.Id == readingId && r.VoidedAt is null);
        if (reading is null)
            return Result.Failure<MeterReading>(MeterErrors.ReadingNotFound);
        GuardValue(value);

        var monotonic = CheckMonotonic(reading.ReadingDate, reading.Kind, SequenceKey(reading), value, exclude: reading);
        if (monotonic.IsFailure)
            return Result.Failure<MeterReading>(monotonic.Error!);

        reading.Correct(value, note);
        return reading;
    }

    /// <summary>Hủy chỉ số chưa khóa (VD chỉ số cuối khi hủy thanh lý) — giữ lịch sử, không xóa.</summary>
    public void VoidReading(Guid readingId, string reason, DateTimeOffset now) =>
        _readings.FirstOrDefault(r => r.Id == readingId && r.VoidedAt is null)?.Void(reason, now);

    private Result CheckMonotonic(DateOnly date, ReadingKind kind, long sequence, decimal value, MeterReading? exclude)
    {
        var key = (date, kind, sequence);
        var others = Ordered.Where(r => r != exclude).ToList();
        var previous = others.LastOrDefault(r => Compare(Key(r), key) < 0);
        var next = others.FirstOrDefault(r => Compare(Key(r), key) > 0);
        return (previous is not null && value < previous.Value) || (next is not null && value > next.Value)
            ? Result.Failure(MeterErrors.NotMonotonic(previous?.Value, next?.Value))
            : Result.Success();
    }

    // Chỉ số chưa lưu có Sequence = 0 ⇒ coi như mới nhất trong cùng ngày + loại.
    private static long SequenceKey(MeterReading r) => r.Sequence == 0 ? long.MaxValue : r.Sequence;

    private static (DateOnly, ReadingKind, long) Key(MeterReading r) => (r.ReadingDate, r.Kind, SequenceKey(r));

    private static int Compare((DateOnly Date, ReadingKind Kind, long Seq) a, (DateOnly Date, ReadingKind Kind, long Seq) b)
    {
        var byDate = a.Date.CompareTo(b.Date);
        if (byDate != 0)
            return byDate;
        var byKind = a.Kind.CompareTo(b.Kind);
        return byKind != 0 ? byKind : a.Seq.CompareTo(b.Seq);
    }

    private static void GuardValue(decimal value)
    {
        if (value < 0 || value > MaxValue)
            throw new ArgumentOutOfRangeException(nameof(value), "Reading value must be between 0 and 99,999,999.99.");
    }
}

public sealed class MeterReading : TenantEntity
{
    private MeterReading() { } // EF Core

    internal MeterReading(Guid meterId, ReadingKind kind, DateOnly readingDate, decimal value, Guid? contractId, string? note)
    {
        Id = Guid.CreateVersion7();
        MeterId = meterId;
        Kind = kind;
        ReadingDate = readingDate;
        Value = value;
        ContractId = contractId;
        Note = TextNormalizer.TrimToNull(note);
    }

    public Guid MeterId { get; private set; }
    public ReadingKind Kind { get; private set; }
    public DateOnly ReadingDate { get; private set; }

    /// <summary>Thứ tự ghi (identity trong DB) — phân định khi cùng ngày, cùng loại.</summary>
    public long Sequence { get; private set; }
    public decimal Value { get; private set; }
    public Guid? ContractId { get; private set; }

    /// <summary>Đợt 2 (M07): kỳ của chỉ số <see cref="ReadingKind.Periodic"/>.</summary>
    public DateOnly? ClosingPeriodStart { get; internal set; }
    public string? Note { get; private set; }
    public DateTimeOffset? VoidedAt { get; private set; }
    public string? VoidReason { get; private set; }

    internal void Void(string reason, DateTimeOffset now)
    {
        VoidedAt = now;
        VoidReason = reason;
    }

    internal void Correct(decimal value, string? note)
    {
        Value = value;
        Note = TextNormalizer.TrimToNull(note) ?? Note;
    }
}

public static class MeterErrors
{
    public static readonly Error NotFound = Error.NotFound("METER_NOT_FOUND", "Không tìm thấy công tơ.");
    public static readonly Error ReadingNotFound = Error.NotFound("READING_NOT_FOUND", "Không tìm thấy chỉ số.");
    public static readonly Error AlreadyActive = Error.Conflict("METER_ALREADY_ACTIVE",
        "Phòng đã có công tơ đang hoạt động cho khoản này — thay công tơ thay vì lắp thêm.");
    public static readonly Error FeeNotMetered = Error.BusinessRule("FEE_NOT_METERED", "Chỉ khoản điện nước theo công tơ mới lắp công tơ.");
    public static readonly Error Removed = Error.BusinessRule("METER_REMOVED", "Công tơ đã tháo.");
    public static readonly Error InvalidReadingDate = Error.BusinessRule("INVALID_READING_DATE",
        "Ngày ghi chỉ số phải trong thời gian công tơ hoạt động và không trước chỉ số đã ghi.");

    public static Error NotMonotonic(decimal? previousValue, decimal? nextValue) =>
        Error.BusinessRule("READING_NOT_MONOTONIC",
                $"Chỉ số phải ≥ chỉ số trước{(previousValue is { } p ? $" ({p:0.##})" : "")}" +
                $"{(nextValue is { } n ? $" và ≤ chỉ số sau ({n:0.##})" : "")}.")
            .WithDetail("previousValue", previousValue)
            .WithDetail("nextValue", nextValue);

    public static Error HandoverReadingRequired(IReadOnlyCollection<Guid> meterIds) =>
        Error.BusinessRule("HANDOVER_READING_REQUIRED",
                "Nhập chỉ số nhận phòng cho từng công tơ của phòng (hoặc chọn \"Dùng số mới nhất\").")
            .WithDetail("meterIds", meterIds);

    public static Error FinalReadingRequired(IReadOnlyCollection<Guid> meterIds) =>
        Error.BusinessRule("FINAL_READING_REQUIRED", "Nhập chỉ số cuối cho từng công tơ của phòng tại ngày trả phòng.")
            .WithDetail("meterIds", meterIds);

    public static readonly Error ReadingLocked = Error.BusinessRule("READING_LOCKED",
        "Chỉ số đã dùng cho phiếu đã chốt — hủy phiếu trước khi sửa.");

    public static readonly Error UnknownMeter = Error.BusinessRule("METER_NOT_IN_ROOM", "Công tơ không thuộc phòng của hợp đồng hoặc không hoạt động tại ngày này.");
}
