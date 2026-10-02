using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using renting_room.Application.Common.Interfaces;
using renting_room.Application.Common.Security;
using renting_room.Domain.Identity;

namespace renting_room.Infrastructure.Identity;

public sealed class JwtTokenService(IOptions<JwtOptions> options, JwtSigningKeyProvider keyProvider) : ITokenService
{
    private const int RefreshTokenBytes = 32;
    private static readonly JsonWebTokenHandler TokenHandler = new();

    private readonly JwtOptions _options = options.Value;

    public TimeSpan RefreshTokenLifetime => TimeSpan.FromDays(_options.RefreshTokenDays);

    public TimeSpan RefreshTokenAbsoluteLifetime => TimeSpan.FromDays(_options.SessionAbsoluteDays);

    public AccessToken CreateAccessToken(User user, DateTimeOffset now)
    {
        var expiresAt = now.AddMinutes(_options.AccessTokenMinutes);

        var claims = new Dictionary<string, object>
        {
            [AppClaimTypes.Subject] = user.Id.ToString(),
            [AppClaimTypes.TokenId] = Guid.NewGuid().ToString(),
            [AppClaimTypes.Role] = user.Role.ToString(),
            [AppClaimTypes.SecurityStamp] = user.SecurityStamp.ToString()
        };

        if (user.OrganizationId is { } organizationId)
            claims[AppClaimTypes.OrganizationId] = organizationId.ToString();

        if (user.MustChangePassword)
            claims[AppClaimTypes.MustChangePassword] = "true";

        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = _options.Issuer,
            Audience = _options.Audience,
            IssuedAt = now.UtcDateTime,
            NotBefore = now.UtcDateTime,
            Expires = expiresAt.UtcDateTime,
            Claims = claims,
            SigningCredentials = new SigningCredentials(keyProvider.Key, SecurityAlgorithms.HmacSha256)
        };

        return new AccessToken(TokenHandler.CreateToken(descriptor), expiresAt);
    }

    public GeneratedRefreshToken GenerateRefreshToken()
    {
        var value = Base64UrlEncoder.Encode(RandomNumberGenerator.GetBytes(RefreshTokenBytes));
        return new GeneratedRefreshToken(value, HashRefreshToken(value));
    }

    /// <summary>SHA-256 hex. Token có 256 bit ngẫu nhiên nên không cần salt/băm chậm như mật khẩu.</summary>
    public string HashRefreshToken(string refreshToken) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(refreshToken))).ToLowerInvariant();
}
