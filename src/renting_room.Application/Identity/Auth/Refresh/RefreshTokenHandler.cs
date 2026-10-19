using Mediator;
using renting_room.Application.Identity.Organizations;
using Microsoft.Extensions.Logging;
using renting_room.Application.Common.Interfaces;
using renting_room.Domain.Common;
using renting_room.Domain.Identity;

namespace renting_room.Application.Identity.Auth.Refresh;

public sealed class RefreshTokenHandler(
    IUserRepository users,
    IUserQueries userQueries,
    IOrganizationQueries organizations,
    IRefreshTokenRepository refreshTokens,
    IUnitOfWork unitOfWork,
    ITokenService tokenService,
    AuthTokenIssuer tokenIssuer,
    IUserSessionStore sessionStore,
    TimeProvider clock,
    ILogger<RefreshTokenHandler> logger)
    : IRequestHandler<RefreshTokenCommand, Result<AuthTokens>>
{
    public async ValueTask<Result<AuthTokens>> Handle(RefreshTokenCommand request, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var tokenHash = tokenService.HashRefreshToken(request.RefreshToken);

        var current = await refreshTokens.FindByHashAsync(tokenHash, cancellationToken);

        if (current is null)
            return IdentityErrors.InvalidRefreshToken;

        if (current.IsRevoked)
        {
            await HandleReusedTokenAsync(current, now, cancellationToken);
            return IdentityErrors.InvalidRefreshToken;
        }

        if (current.IsExpired(now) || current.FamilyExpiresAt <= now)
            return IdentityErrors.InvalidRefreshToken;

        var user = await userQueries.GetAsync(current.UserId, cancellationToken);
        if (user is null || user.Status != UserStatus.Active || user.SecurityStamp != current.SecurityStamp)
        {
            await refreshTokens.RevokeFamilyAsync(current.FamilyId, RefreshTokenRevokeReason.AdminAction, now, cancellationToken);
            return IdentityErrors.InvalidRefreshToken;
        }

        if (user.OrganizationId is { } organizationId)
        {
            var isOrganizationActive = await organizations.GetStatusAsync(organizationId, cancellationToken) == OrganizationStatus.Active;
            if (!isOrganizationActive)
                return IdentityErrors.OrganizationSuspended;
        }

        var (tokens, successor) = tokenIssuer.IssueRotation(user, current, now, request.IpAddress);

        await using var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken);

        // Chỉ một request được xoay vòng token này: điều kiện RevokedAt IS NULL chặn request song song.
        var rotated = await refreshTokens.MarkRotatedAsync(current.Id, successor.Id, now, cancellationToken);

        if (rotated == 0)
        {
            await transaction.RollbackAsync(cancellationToken);
            return IdentityErrors.InvalidRefreshToken;
        }

        refreshTokens.Add(successor);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return tokens;
    }

    /// <summary>
    /// Token đã thu hồi bị dùng lại: nếu vừa xoay vòng trong grace period thì là request song song hợp lệ (chỉ từ chối);
    /// ngược lại nghi bị đánh cắp → thu hồi toàn bộ family VÀ đổi security stamp để access token kẻ gian
    /// đã lấy được cũng mất hiệu lực ngay (ID-BR-09). User thật phải đăng nhập lại.
    /// </summary>
    private async Task HandleReusedTokenAsync(RefreshToken token, DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (!token.IsSuspiciousReuse(now))
            return;

        await using var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken);

        var revoked = await refreshTokens.RevokeFamilyAsync(token.FamilyId, RefreshTokenRevokeReason.Reuse, now, cancellationToken);
        await users.RotateSecurityStampAsync(token.UserId, cancellationToken);

        await transaction.CommitAsync(cancellationToken);
        sessionStore.Invalidate(token.UserId);

        logger.LogWarning(
            "Refresh token reuse detected for user {UserId}, family {FamilyId}; revoked {Count} active token(s) and all access tokens",
            token.UserId, token.FamilyId, revoked);
    }
}
