using renting_room.Domain.Common;
using renting_room.Domain.Contracts;
using renting_room.Domain.Identity;

namespace renting_room.Infrastructure.Auditing;

/// <summary>Quy tắc chọn dữ liệu đưa vào audit log (C-10).</summary>
public static class AuditPolicy
{
    /// <summary>Thay cho giá trị trường nhạy cảm — chỉ cho biết trường có giá trị / đã đổi, không lộ nội dung.</summary>
    public const string RedactedValue = "[redacted]";

    /// <summary>Thay đổi liên tục, không mang nghĩa nghiệp vụ; sự kiện đăng nhập ghi ở bảng riêng (M01 §11).</summary>
    private static readonly HashSet<Type> IgnoredEntityTypes = [typeof(RefreshToken)];

    /// <summary>Đã có cột riêng trên dòng audit (entity_id, organization_id, user_id, occurred_at) hoặc là token concurrency.</summary>
    private static readonly HashSet<string> IgnoredProperties =
    [
        nameof(Entity.Id),
        nameof(ITenantEntity.OrganizationId),
        nameof(AuditableEntity.Version),
        nameof(AuditableEntity.CreatedAt),
        nameof(AuditableEntity.CreatedBy),
        nameof(AuditableEntity.UpdatedAt),
        nameof(AuditableEntity.UpdatedBy)
    ];

    /// <summary>Không bắt được theo hậu tố: snapshot ký hợp đồng chứa số giấy tờ (đã mã hóa) của các bên.</summary>
    private static readonly HashSet<string> SensitiveProperties = [nameof(User.SecurityStamp), nameof(Contract.SigningSnapshot), nameof(Organization.DefaultLessor)];

    public static bool IsAudited(Type entityType) =>
        typeof(Entity).IsAssignableFrom(entityType) && !IgnoredEntityTypes.Contains(entityType);

    public static bool IsIgnored(string propertyName) => IgnoredProperties.Contains(propertyName);

    /// <summary>Số giấy tờ (bản mã hóa / HMAC), mật khẩu, token, mọi cột nhị phân ⇒ chỉ ghi <see cref="RedactedValue"/>.</summary>
    public static bool IsSensitive(string propertyName, Type clrType) =>
        clrType == typeof(byte[])
        || propertyName.EndsWith("Encrypted", StringComparison.Ordinal)
        || propertyName.EndsWith("Hash", StringComparison.Ordinal)
        || SensitiveProperties.Contains(propertyName);
}
