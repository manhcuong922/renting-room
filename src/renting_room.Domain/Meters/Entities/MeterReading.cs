using renting_room.Domain.Common;

namespace renting_room.Domain.Meters;

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

    /// <returns>true nếu có giá trị đổi.</returns>
    internal bool Correct(decimal value, string? note)
    {
        var text = TextNormalizer.TrimToNull(note) ?? Note;
        if (Value == value && Note == text)
            return false;
        Value = value;
        Note = text;
        return true;
    }
}
