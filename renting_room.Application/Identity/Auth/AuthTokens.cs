using renting_room.Application.Common.Interfaces;
using renting_room.Domain.Identity;

namespace renting_room.Application.Identity.Auth;

public sealed record AuthTokens(
    string AccessToken,
    DateTimeOffset AccessTokenExpiresAt,
    string RefreshToken,
    DateTimeOffset RefreshTokenExpiresAt,
    bool MustChangePassword);

/// <summary>Tạo cặp access token + refresh token (dùng chung bởi login, refresh, đổi mật khẩu).</summary>
public sealed class AuthTokenIssuer(ITokenService tokenService)
{
    /// <summary>Phiên mới (đăng nhập, đổi mật khẩu): family mới với hạn tuyệt đối mới.</summary>
    public (AuthTokens Tokens, RefreshToken Entity) IssueNewSession(User user, DateTimeOffset now, string? ipAddress) =>
        Issue(user, Guid.NewGuid(), now.Add(tokenService.RefreshTokenAbsoluteLifetime), now, ipAddress);

    /// <summary>Xoay vòng: giữ family và hạn tuyệt đối của token hiện tại.</summary>
    public (AuthTokens Tokens, RefreshToken Entity) IssueRotation(
        User user, RefreshToken current, DateTimeOffset now, string? ipAddress) =>
        Issue(user, current.FamilyId, current.FamilyExpiresAt, now, ipAddress);

    private (AuthTokens Tokens, RefreshToken Entity) Issue(
        User user, Guid familyId, DateTimeOffset familyExpiresAt, DateTimeOffset now, string? ipAddress)
    {
        var accessToken = tokenService.CreateAccessToken(user, now);
        var refreshToken = tokenService.GenerateRefreshToken();

        var entity = RefreshToken.Issue(
            user.Id,
            familyId,
            refreshToken.Hash,
            user.SecurityStamp,
            now,
            tokenService.RefreshTokenLifetime,
            familyExpiresAt,
            ipAddress);

        var tokens = new AuthTokens(
            accessToken.Value,
            accessToken.ExpiresAt,
            refreshToken.Value,
            entity.ExpiresAt,
            user.MustChangePassword);

        return (tokens, entity);
    }
}
