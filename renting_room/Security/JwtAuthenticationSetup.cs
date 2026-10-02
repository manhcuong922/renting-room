using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using renting_room.Application.Common.Interfaces;
using renting_room.Application.Common.Security;
using renting_room.Errors;
using renting_room.Infrastructure.Identity;

namespace renting_room.Security;

internal static class JwtAuthenticationSetup
{
    private static readonly TimeSpan AllowedClockSkew = TimeSpan.FromSeconds(30);

    public static IServiceCollection AddJwtAuthentication(this IServiceCollection services)
    {
        services
            .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer();

        // Cấu hình trễ để dùng chung khóa ký với JwtTokenService (JwtSigningKeyProvider là singleton).
        services
            .AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<IOptions<JwtOptions>, JwtSigningKeyProvider>((options, jwtOptions, keyProvider) =>
            {
                options.MapInboundClaims = false; // giữ nguyên tên claim "sub", "role", "org"...
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = jwtOptions.Value.Issuer,
                    ValidateAudience = true,
                    ValidAudience = jwtOptions.Value.Audience,
                    ValidateLifetime = true,
                    RequireExpirationTime = true,
                    ClockSkew = AllowedClockSkew,
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = keyProvider.Key,
                    ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
                    NameClaimType = AppClaimTypes.Subject,
                    RoleClaimType = AppClaimTypes.Role
                };
                options.Events = new JwtBearerEvents
                {
                    OnTokenValidated = ValidateSessionAsync,
                    OnChallenge = WriteUnauthorizedAsync
                };
            });

        return services;
    }

    /// <summary>
    /// Chữ ký hợp lệ chưa đủ: kiểm tra phiên còn hiệu lực (đổi mật khẩu, đăng xuất mọi nơi, khóa user,
    /// tạm ngưng tổ chức đều làm token cũ mất hiệu lực — ID-BR-08).
    /// </summary>
    private static async Task ValidateSessionAsync(TokenValidatedContext context)
    {
        var principal = context.Principal;
        if (!Guid.TryParse(principal?.FindFirst(AppClaimTypes.Subject)?.Value, out var userId)
            || !Guid.TryParse(principal?.FindFirst(AppClaimTypes.SecurityStamp)?.Value, out var stamp))
        {
            context.Fail(new SecurityTokenValidationException("Token is missing required claims."));
            return;
        }

        var sessionStore = context.HttpContext.RequestServices.GetRequiredService<IUserSessionStore>();
        if (!await sessionStore.IsValidAsync(userId, stamp, context.HttpContext.RequestAborted))
            context.Fail(new SessionRevokedException());
    }

    private static async Task WriteUnauthorizedAsync(JwtBearerChallengeContext context)
    {
        context.HandleResponse();

        var (code, detail) = context.AuthenticateFailure switch
        {
            SecurityTokenExpiredException => ("TOKEN_EXPIRED", "Access token đã hết hạn. Hãy làm mới token."),
            SessionRevokedException => ("SESSION_REVOKED", "Phiên đăng nhập đã bị thu hồi. Vui lòng đăng nhập lại."),
            not null => ("INVALID_TOKEN", "Access token không hợp lệ."),
            null => ("AUTHENTICATION_REQUIRED", "Bạn cần đăng nhập để thực hiện thao tác này.")
        };

        context.Response.Headers.WWWAuthenticate = context.AuthenticateFailure is null
            ? "Bearer"
            : "Bearer error=\"invalid_token\"";

        await ProblemResponses.WriteAsync(context.HttpContext, StatusCodes.Status401Unauthorized, code, detail);
    }

    private sealed class SessionRevokedException() : SecurityTokenValidationException("Session has been revoked.");
}
