using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using renting_room.Application.Common.Interfaces;

namespace renting_room.Infrastructure.Auditing;

/// <summary>
/// Một dòng nhật ký kiểm toán (C-10): ai, làm gì, với đối tượng nào, lúc nào, từ IP nào, thay đổi ra sao.
/// Bảng kỹ thuật — không thuộc global filter tổ chức, chỉ thêm mới (không sửa / xóa trừ khi ẩn danh hóa).
/// </summary>
public sealed class AuditLog
{
    private AuditLog() { } // EF Core

    public Guid Id { get; private set; }
    public Guid? OrganizationId { get; private set; }
    public Guid? UserId { get; private set; }
    public string Action { get; private set; } = null!;
    public string EntityType { get; private set; } = null!;
    public Guid? EntityId { get; private set; }

    /// <summary>JSON: thay đổi (sửa: <c>{"field":{"old":…,"new":…}}</c>; tạo / xóa: giá trị) hoặc chi tiết sự kiện.</summary>
    public string? Changes { get; private set; }

    public string? IpAddress { get; private set; }
    public DateTimeOffset OccurredAt { get; private set; }

    internal static AuditLog Create(
        AuditActor actor, Guid? organizationId, string action, string entityType, Guid? entityId, string? changes) => new()
    {
        // UUID v7 theo thời gian ⇒ khóa chính tăng dần, chèn cuối index thay vì rải ngẫu nhiên.
        Id = Guid.CreateVersion7(actor.OccurredAt),
        OrganizationId = organizationId,
        UserId = actor.UserId,
        Action = action,
        EntityType = entityType,
        EntityId = entityId,
        Changes = changes,
        IpAddress = actor.IpAddress,
        OccurredAt = actor.OccurredAt
    };
}

/// <summary>Người thực hiện + thời điểm, chụp một lần cho mọi dòng audit của cùng thao tác.</summary>
internal sealed record AuditActor(Guid? UserId, Guid? OrganizationId, string? IpAddress, DateTimeOffset OccurredAt)
{
    public static AuditActor From(ICurrentUser currentUser, TimeProvider clock) =>
        new(currentUser.UserId, currentUser.OrganizationId, Truncate(currentUser.IpAddress), clock.GetUtcNow());

    /// <summary>IPv6 kèm zone id (<c>fe80::1%eth0</c>) có thể vượt cột — cắt bớt thay vì làm hỏng cả lần lưu / cả lô ghi nền.</summary>
    private static string? Truncate(string? ipAddress) =>
        ipAddress is { Length: > AuditLogConfiguration.MaxIpLength } ? ipAddress[..AuditLogConfiguration.MaxIpLength] : ipAddress;
}

internal sealed class AuditLogConfiguration : IEntityTypeConfiguration<AuditLog>
{
    public const int MaxNameLength = 64;
    public const int MaxIpLength = 45; // IPv6 dạng chữ dài nhất

    public void Configure(EntityTypeBuilder<AuditLog> builder)
    {
        builder.ToTable("audit_logs");
        builder.HasKey(a => a.Id);
        builder.Property(a => a.Id).ValueGeneratedNever();

        builder.Property(a => a.Action).HasMaxLength(MaxNameLength);
        builder.Property(a => a.EntityType).HasMaxLength(MaxNameLength);
        builder.Property(a => a.Changes).HasColumnType("jsonb");
        builder.Property(a => a.IpAddress).HasMaxLength(MaxIpLength);

        // Chỉ 2 index: tra lịch sử 1 đối tượng, và nhật ký theo thời gian của tổ chức. Mỗi index thêm làm chậm mọi lần ghi.
        builder.HasIndex(a => new { a.OrganizationId, a.EntityType, a.EntityId });
        builder.HasIndex(a => new { a.OrganizationId, a.OccurredAt });
    }
}
