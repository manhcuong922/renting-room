using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace renting_room.Infrastructure.Idempotency;

public static class IdempotencyStatus
{
    public const string InProgress = "InProgress";
    public const string Completed = "Completed";
}

/// <summary>Bản ghi kỹ thuật (không phải nghiệp vụ) — phạm vi theo user, không thuộc global filter tổ chức.</summary>
public sealed class IdempotencyRecord
{
    public Guid UserId { get; set; }
    public string Key { get; set; } = null!;
    public string RequestHash { get; set; } = null!;
    public string Method { get; set; } = null!;
    public string Path { get; set; } = null!;
    public string Status { get; set; } = null!;
    public int? ResponseStatusCode { get; set; }
    public string? ResponseContentType { get; set; }
    public string? ResponseLocation { get; set; }

    /// <summary>Body response đã mã hóa (Data Protection) — có thể chứa token / mật khẩu tạm.</summary>
    public byte[]? ResponseBody { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
}

internal sealed class IdempotencyRecordConfiguration : IEntityTypeConfiguration<IdempotencyRecord>
{
    public void Configure(EntityTypeBuilder<IdempotencyRecord> builder)
    {
        builder.ToTable("idempotency_keys", t => t.HasCheckConstraint(
            "ck_idempotency_keys_status", "status IN ('InProgress', 'Completed')"));

        // Khóa chính chính là chốt chặn race condition: 2 request cùng key chỉ 1 INSERT thành công.
        builder.HasKey(r => new { r.UserId, r.Key });

        builder.Property(r => r.Key).HasMaxLength(IdempotencyOptions.MaxKeyLength);
        builder.Property(r => r.RequestHash).HasMaxLength(64).IsFixedLength();
        builder.Property(r => r.Method).HasMaxLength(10);
        builder.Property(r => r.Path).HasMaxLength(500);
        builder.Property(r => r.Status).HasMaxLength(16);
        builder.Property(r => r.ResponseContentType).HasMaxLength(100);
        builder.Property(r => r.ResponseLocation).HasMaxLength(500);

        builder.HasIndex(r => r.ExpiresAt).HasDatabaseName("ix_idempotency_keys_expires_at");
    }
}

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
