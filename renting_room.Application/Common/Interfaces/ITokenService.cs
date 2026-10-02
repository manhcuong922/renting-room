using renting_room.Domain.Identity;

namespace renting_room.Application.Common.Interfaces;

public sealed record AccessToken(string Value, DateTimeOffset ExpiresAt);

/// <summary>Refresh token gốc (trả client) và hash (lưu DB).</summary>
public sealed record GeneratedRefreshToken(string Value, string Hash);

public interface ITokenService
{
    /// <summary>Hạn của mỗi refresh token (trượt theo mỗi lần xoay vòng).</summary>
    TimeSpan RefreshTokenLifetime { get; }

    /// <summary>Hạn tuyệt đối của một phiên đăng nhập — hết hạn thì phải đăng nhập lại.</summary>
    TimeSpan RefreshTokenAbsoluteLifetime { get; }

    AccessToken CreateAccessToken(User user, DateTimeOffset now);

    GeneratedRefreshToken GenerateRefreshToken();

    string HashRefreshToken(string refreshToken);
}
