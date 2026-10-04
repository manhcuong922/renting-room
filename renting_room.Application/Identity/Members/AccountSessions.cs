using renting_room.Application.Common.Interfaces;
using renting_room.Application.Identity.Auth;
using renting_room.Domain.Identity;

namespace renting_room.Application.Identity.Members;

/// <summary>
/// Sau khi khóa / gỡ / cấp lại mật khẩu (stamp đã đổi ở domain): thu hồi mọi refresh token và xóa cache phiên
/// ⇒ tài khoản bị đá ra ngay (ID-BR-16). Gọi TRONG transaction cùng lệnh lưu user.
/// </summary>
internal static class AccountSessions
{
    public static Task RevokeAllAsync(IAppDbContext db, Guid userId, DateTimeOffset now, CancellationToken ct) =>
        RefreshTokenRevoker.RevokeAllForUserAsync(db, userId, RefreshTokenRevokeReason.AdminAction, now, ct);
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
    string Version);

/// <summary>Mật khẩu tạm chỉ trả về đúng một lần — không lưu dạng rõ, không ghi log.</summary>
public sealed record TemporaryCredentials(Guid UserId, string Username, string TemporaryPassword, DateTimeOffset ExpiresAt);

internal static class MemberProjection
{
    public static IQueryable<MemberDto> ToDto(this IQueryable<User> users) =>
        users.Select(u => new MemberDto(
            u.Id,
            u.FullName,
            u.PhoneNormalized,
            u.Status == UserStatus.Removed ? null : u.EmailNormalized,
            u.Role,
            u.Status,
            u.MustChangePassword,
            u.TempPasswordExpiresAt,
            u.LastLoginAt,
            u.CreatedAt,
            u.Role == UserRole.OrgOwner || u.CanViewSensitiveData,
            u.Version.ToString()));
}
