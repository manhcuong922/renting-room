using renting_room.Domain.Common;

namespace renting_room.Domain.Identity;

/// <summary>
/// Refresh token dùng một lần. Chỉ lưu hash SHA-256; token gốc chỉ trả cho client một lần.
/// Các token sinh ra từ cùng một lần đăng nhập chung <see cref="FamilyId"/> để phát hiện tái sử dụng (ID-BR-09).
/// </summary>
public sealed class RefreshToken : Entity
{
    /// <summary>Hai request refresh song song (nhiều tab) trong khoảng này không bị coi là tấn công tái sử dụng.</summary>
    public static readonly TimeSpan ReuseGracePeriod = TimeSpan.FromSeconds(10);

    private RefreshToken() { } // EF Core

    public Guid UserId { get; private set; }
    public Guid FamilyId { get; private set; }
    public string TokenHash { get; private set; } = null!;
    public Guid SecurityStamp { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset ExpiresAt { get; private set; }

    /// <summary>
    /// Hạn tuyệt đối của cả phiên (family): xoay vòng không kéo dài quá mốc này, nên một phiên bị đánh cắp
    /// mà chưa bị phát hiện cũng không sống mãi.
    /// </summary>
    public DateTimeOffset FamilyExpiresAt { get; private set; }

    public string? CreatedIp { get; private set; }
    public DateTimeOffset? RevokedAt { get; private set; }
    public RefreshTokenRevokeReason? RevokedReason { get; private set; }
    public Guid? ReplacedById { get; private set; }

    public static RefreshToken Issue(
        Guid userId,
        Guid familyId,
        string tokenHash,
        Guid securityStamp,
        DateTimeOffset now,
        TimeSpan lifetime,
        DateTimeOffset familyExpiresAt,
        string? createdIp)
    {
        if (string.IsNullOrWhiteSpace(tokenHash))
            throw new ArgumentException("Token hash is required.", nameof(tokenHash));
        if (lifetime <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(lifetime));
        if (familyExpiresAt <= now)
            throw new ArgumentOutOfRangeException(nameof(familyExpiresAt), "Session family has already expired.");

        var slidingExpiry = now.Add(lifetime);

        return new RefreshToken
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            FamilyId = familyId,
            TokenHash = tokenHash,
            SecurityStamp = securityStamp,
            CreatedAt = now,
            ExpiresAt = slidingExpiry < familyExpiresAt ? slidingExpiry : familyExpiresAt,
            FamilyExpiresAt = familyExpiresAt,
            CreatedIp = createdIp
        };
    }

    public bool IsRevoked => RevokedAt is not null;

    public bool IsExpired(DateTimeOffset now) => now >= ExpiresAt;

    /// <summary>Token vừa bị xoay vòng rất gần đây — nhiều khả năng là request song song hợp lệ, không phải bị đánh cắp.</summary>
    public bool WasRotatedWithinGracePeriod(DateTimeOffset now) =>
        RevokedReason == RefreshTokenRevokeReason.Rotated
        && RevokedAt is not null
        && now - RevokedAt.Value <= ReuseGracePeriod;

    /// <summary>
    /// Dấu hiệu bị đánh cắp: token ĐÃ BỊ XOAY VÒNG (tức đã có token kế nhiệm) mà vẫn được dùng lại sau grace period
    /// ⇒ tồn tại một bản sao ở nơi khác. Token bị thu hồi vì đăng xuất / đổi mật khẩu / admin thì chỉ từ chối,
    /// không coi là tấn công (nếu không, client cũ gửi lại token sau khi đổi mật khẩu sẽ đá văng phiên mới hợp lệ).
    /// </summary>
    public bool IsSuspiciousReuse(DateTimeOffset now) =>
        RevokedReason == RefreshTokenRevokeReason.Rotated && !WasRotatedWithinGracePeriod(now);
}
