using System.ComponentModel.DataAnnotations;

namespace renting_room.Infrastructure.Idempotency;

public sealed class IdempotencyOptions
{
    public const string SectionName = "Idempotency";
    public const int MaxKeyLength = 64;

    /// <summary>Giữ kết quả bao lâu (client retry trong khoảng này nhận lại response cũ).</summary>
    [Range(1, 168)]
    public int RetentionHours { get; init; } = 24;

    /// <summary>Request "đang chạy" quá thời gian này coi như đã chết (process crash) — cho phép chạy lại.</summary>
    [Range(10, 3600)]
    public int InProgressTimeoutSeconds { get; init; } = 120;

    /// <summary>Response lớn hơn ngưỡng không được lưu (endpoint áp idempotency chỉ trả body nhỏ).</summary>
    [Range(1024, 4_194_304)]
    public int MaxStoredResponseBytes { get; init; } = 262_144;
}
