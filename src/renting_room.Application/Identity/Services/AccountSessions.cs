using renting_room.Domain.Identity;

namespace renting_room.Application.Identity.Members;

/// <summary>
/// Sau khi khóa / gỡ / cấp lại mật khẩu (stamp đã đổi ở domain): thu hồi mọi refresh token và xóa cache phiên
/// ⇒ tài khoản bị đá ra ngay (ID-BR-16). Gọi TRONG transaction cùng lệnh lưu user.
/// </summary>
internal static class AccountSessions
{
    public static Task RevokeAllAsync(IRefreshTokenRepository refreshTokens, Guid userId, DateTimeOffset now, CancellationToken ct) =>
        refreshTokens.RevokeAllForUserAsync(userId, RefreshTokenRevokeReason.AdminAction, now, ct);
}

public sealed record MemberDto(
    Guid Id,
    string FullName,
    string? Phone,
    string? Email,
    UserRole Role,
    UserStatus Status,
    bool MustChangePassword,
    DateTimeOffset? TempPasswordExpiresAt,
    DateTimeOffset? LastLoginAt,
    DateTimeOffset CreatedAt,
    bool CanViewSensitiveData,
    string Version,
    bool CanWriteOff = false);

/// <summary>Mật khẩu tạm chỉ trả về đúng một lần — không lưu dạng rõ, không ghi log.</summary>
public sealed record TemporaryCredentials(Guid UserId, string Username, string TemporaryPassword, DateTimeOffset ExpiresAt);
