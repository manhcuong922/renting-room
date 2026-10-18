using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace renting_room.Infrastructure.Identity;

/// <summary>Nguồn duy nhất của khóa ký JWT — dùng chung cho phát hành (TokenService) và xác thực (JwtBearer).</summary>
public sealed class JwtSigningKeyProvider
{
    public JwtSigningKeyProvider(
        IOptions<JwtOptions> options,
        IHostEnvironment environment,
        ILogger<JwtSigningKeyProvider> logger)
    {
        var configuredKey = options.Value.SigningKey;

        if (!string.IsNullOrWhiteSpace(configuredKey))
        {
            var keyBytes = Encoding.UTF8.GetBytes(configuredKey);
            if (keyBytes.Length < JwtOptions.MinSigningKeyBytes)
                throw new InvalidOperationException(
                    $"Jwt:SigningKey must be at least {JwtOptions.MinSigningKeyBytes} bytes.");

            Key = new SymmetricSecurityKey(keyBytes);
            return;
        }

        if (!environment.IsDevelopment())
            throw new InvalidOperationException(
                "Jwt:SigningKey is not configured. Set it via environment variable Jwt__SigningKey or a secret manager.");

        logger.LogWarning(
            "Jwt:SigningKey is not configured; using an ephemeral development key. Tokens become invalid after restart.");
        Key = new SymmetricSecurityKey(RandomNumberGenerator.GetBytes(64));
    }

    public SymmetricSecurityKey Key { get; }
}
