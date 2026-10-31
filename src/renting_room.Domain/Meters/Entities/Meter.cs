using renting_room.Domain.Common;

namespace renting_room.Domain.Meters;

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
        meter.Raise(new MeterChanged(meter.Id, meter.RoomId));
        meter.Raise(new MeterReadingChanged(meter.Id));
        return meter;
    }

    /// <summary>
    /// Thứ tự cùng ngày (M-10, MT-BR-03): HĐ vào trước xếp trước HĐ vào sau (theo lần ghi đầu tiên của HĐ trên công tơ), trong một HĐ theo loại rồi
    /// <c>sequence</c>; chỉ số lắp luôn đầu, chỉ số tháo luôn cuối.
    /// </summary>
    public IEnumerable<MeterReading> Ordered
    {
        get
        {
            var live = _readings.Where(r => r.VoidedAt is null).ToList();
            var groups = GroupSequences(live);
            return live.OrderBy(r => r.ReadingDate).ThenBy(r => GroupOf(r, groups)).ThenBy(r => r.Kind).ThenBy(SequenceKey);
        }
    }

    public MeterReading? LatestOnOrBefore(DateOnly date) => Ordered.LastOrDefault(r => r.ReadingDate <= date);

    public MeterReading? Latest => Ordered.LastOrDefault();

    public MeterReading? Find(Guid readingId) => _readings.FirstOrDefault(r => r.Id == readingId && r.VoidedAt is null);

    public MeterReading? FindPeriodic(Guid contractId, DateOnly closingPeriodStart) =>
        Ordered.FirstOrDefault(r => r.Kind == ReadingKind.Periodic && r.ContractId == contractId && r.ClosingPeriodStart == closingPeriodStart);

    public MeterReading? FindFinal(Guid contractId, DateOnly? since = null, DateOnly? until = null) =>
        Ordered.LastOrDefault(r => r.Kind == ReadingKind.Final && r.ContractId == contractId && (since is null || r.ReadingDate >= since)
            && (until is null || r.ReadingDate <= until));

    public MeterReading? FindHandover(Guid contractId, DateOnly? from = null, DateOnly? to = null) =>
        Ordered.LastOrDefault(r => r.Kind == ReadingKind.Handover && r.ContractId == contractId
            && (from is null || r.ReadingDate >= from) && (to is null || r.ReadingDate <= to));

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

        var monotonic = CheckMonotonic(date, kind, long.MaxValue, value, exclude: null, contractId);
        if (monotonic.IsFailure)
            return Result.Failure<MeterReading>(monotonic.Error!);

        var reading = new MeterReading(Id, kind, date, value, contractId, note) { ClosingPeriodStart = closingPeriodStart };
        _readings.Add(reading);
        Raise(new MeterReadingChanged(Id));
        return reading;
    }

    public Result Remove(DateOnly date, decimal finalValue, string? note)
    {
        if (!IsActive)
            return Result.Failure(MeterErrors.Removed);
        GuardValue(finalValue);
        if (date < InstalledDate || Ordered.Any(r => r.ReadingDate > date))
            return Result.Failure(MeterErrors.InvalidReadingDate);

        var monotonic = CheckMonotonic(date, ReadingKind.Removal, long.MaxValue, finalValue, exclude: null, contractId: null);
        if (monotonic.IsFailure)
            return monotonic;

        _readings.Add(new MeterReading(Id, ReadingKind.Removal, date, finalValue, null, note));
        RemovedDate = date;
        Raise(new MeterReadingChanged(Id));
        Raise(new MeterChanged(Id, RoomId));
        return Result.Success();
    }

    public Result<Meter> ReplaceWith(DateOnly date, decimal oldFinalValue, string? newSerialNo, decimal newInitialValue, string? note)
    {
        var removed = Remove(date, oldFinalValue, note);
        if (removed.IsFailure)
            return Result.Failure<Meter>(removed.Error!);

        var next = Install(PropertyId, RoomId, FeeTypeId, newSerialNo, date, newInitialValue, note);
        ReplacedByMeterId = next.Id;
        Raise(new MeterChanged(Id, RoomId));
        return next;
    }

    /// <param name="date">Ngày ghi mới (lưới chỉ số gửi lại dòng đã có số); null = giữ ngày.</param>
    public Result<MeterReading> Correct(Guid readingId, decimal value, string? note, DateOnly? date = null)
    {
        var reading = _readings.FirstOrDefault(r => r.Id == readingId && r.VoidedAt is null);
        if (reading is null)
            return Result.Failure<MeterReading>(MeterErrors.ReadingNotFound);
        GuardValue(value);
        var readingDate = date ?? reading.ReadingDate;
        if (readingDate != reading.ReadingDate && (readingDate < InstalledDate || (RemovedDate is { } removed && readingDate >= removed)))
            return Result.Failure<MeterReading>(MeterErrors.InvalidReadingDate);

        var monotonic = CheckMonotonic(readingDate, reading.Kind, SequenceKey(reading), value, exclude: reading, reading.ContractId);
        if (monotonic.IsFailure)
            return Result.Failure<MeterReading>(monotonic.Error!);

        if (reading.Correct(value, note, readingDate))
            Raise(new MeterReadingChanged(Id));
        return reading;
    }

    public Result VoidEntry(Guid readingId, string reason, DateTimeOffset now)
    {
        var reading = Find(readingId);
        if (reading is null)
            return Result.Failure(MeterErrors.ReadingNotFound);
        if (reading.Kind is not (ReadingKind.Periodic or ReadingKind.Adhoc))
            return Result.Failure(MeterErrors.ReadingNotVoidable);

        reading.Void(reason.Trim(), now);
        Raise(new MeterReadingChanged(Id));
        return Result.Success();
    }

    /// <summary>Hủy chỉ số chưa khóa (VD chỉ số cuối khi hủy thanh lý) — giữ lịch sử, không xóa.</summary>
    public void VoidReading(Guid readingId, string reason, DateTimeOffset now)
    {
        if (_readings.FirstOrDefault(r => r.Id == readingId && r.VoidedAt is null) is not { } reading)
            return;
        reading.Void(reason, now);
        Raise(new MeterReadingChanged(Id));
    }

    /// <summary>
    /// Đơn điệu theo HĐ (M-10): chỉ so với chỉ số cùng HĐ, chỉ số không thuộc HĐ nào (lắp, kiểm tra), và chỉ số tháo khi đây là HĐ cuối cùng của công tơ. Chỉ số tháo
    /// (<paramref name="contractId"/> null) so với toàn bộ chỉ số liền trước.
    /// </summary>
    private Result CheckMonotonic(DateOnly date, ReadingKind kind, long sequence, decimal value, MeterReading? exclude, Guid? contractId)
    {
        var others = Ordered.Where(r => r != exclude).ToList();
        var groups = GroupSequences(others);
        var key = (date, contractId is { } c && groups.TryGetValue(c, out var own) ? own : long.MaxValue, kind, sequence);
        var lastContractAfter = others.LastOrDefault(r => r.ContractId is not null && Compare(Key(r, groups), key) > 0);
        var lane = contractId is null
            ? others
            : others.Where(r => r.ContractId == contractId
                || (r.ContractId is null && (r.Kind != ReadingKind.Removal || lastContractAfter is null || lastContractAfter.ContractId == contractId))).ToList();
        var previous = lane.LastOrDefault(r => Compare(Key(r, groups), key) < 0);
        var next = lane.FirstOrDefault(r => Compare(Key(r, groups), key) > 0);
        return (previous is not null && value < previous.Value) || (next is not null && value > next.Value)
            ? Result.Failure(MeterErrors.NotMonotonic(previous?.Value, next?.Value))
            : Result.Success();
    }

    // Chỉ số chưa lưu có Sequence = 0 ⇒ coi như mới hơn mọi chỉ số đã lưu, theo thứ tự thêm vào; chỉ số đang kiểm (chưa thêm) mới nhất.
    private long SequenceKey(MeterReading r) => r.Sequence != 0 ? r.Sequence : long.MaxValue / 2 + _readings.IndexOf(r);

    /// <summary>Thứ tự vào công tơ của từng HĐ = <c>sequence</c> nhỏ nhất trong các chỉ số của HĐ đó.</summary>
    private Dictionary<Guid, long> GroupSequences(IEnumerable<MeterReading> readings) =>
        readings.Where(r => r.ContractId is not null).GroupBy(r => r.ContractId!.Value).ToDictionary(g => g.Key, g => g.Min(SequenceKey));

    private static long GroupOf(MeterReading r, Dictionary<Guid, long> groups) =>
        r.Kind == ReadingKind.Initial ? long.MinValue
        : r.ContractId is { } c ? groups[c]
        : long.MaxValue;

    private (DateOnly, long, ReadingKind, long) Key(MeterReading r, Dictionary<Guid, long> groups) =>
        (r.ReadingDate, GroupOf(r, groups), r.Kind, SequenceKey(r));

    private static int Compare((DateOnly Date, long Group, ReadingKind Kind, long Seq) a, (DateOnly Date, long Group, ReadingKind Kind, long Seq) b)
    {
        var byDate = a.Date.CompareTo(b.Date);
        if (byDate != 0)
            return byDate;
        var byGroup = a.Group.CompareTo(b.Group);
        if (byGroup != 0)
            return byGroup;
        var byKind = a.Kind.CompareTo(b.Kind);
        return byKind != 0 ? byKind : a.Seq.CompareTo(b.Seq);
    }

    private static void GuardValue(decimal value)
    {
        if (value < 0 || value > MaxValue)
            throw new ArgumentOutOfRangeException(nameof(value), "Reading value must be between 0 and 99,999,999.99.");
    }
}
