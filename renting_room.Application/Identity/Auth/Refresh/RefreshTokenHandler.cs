using Mediator;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using renting_room.Application.Common.Interfaces;
using renting_room.Domain.Common;
using renting_room.Domain.Identity;

namespace renting_room.Application.Identity.Auth.Refresh;

public sealed class RefreshTokenHandler(
    IAppDbContext db,
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

        var current = await db.RefreshTokens
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.TokenHash == tokenHash, cancellationToken);

        if (current is null)
            return IdentityErrors.InvalidRefreshToken;

        if (current.IsRevoked)
        {
            await HandleReusedTokenAsync(current, now, cancellationToken);
            return IdentityErrors.InvalidRefreshToken;
        }

        if (current.IsExpired(now) || current.FamilyExpiresAt <= now)
            return IdentityErrors.InvalidRefreshToken;

        var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == current.UserId, cancellationToken);
        if (user is null || user.Status != UserStatus.Active || user.SecurityStamp != current.SecurityStamp)
        {
            await RefreshTokenRevoker.RevokeFamilyAsync(
                db, current.FamilyId, RefreshTokenRevokeReason.AdminAction, now, cancellationToken);
            return IdentityErrors.InvalidRefreshToken;
        }

        if (user.OrganizationId is { } organizationId)
        {
            var isOrganizationActive = await db.Organizations
                .AnyAsync(o => o.Id == organizationId && o.Status == OrganizationStatus.Active, cancellationToken);
            if (!isOrganizationActive)
                return IdentityErrors.OrganizationSuspended;
        }

        var (tokens, successor) = tokenIssuer.IssueRotation(user, current, now, request.IpAddress);

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        // Chỉ một request được xoay vòng token này: điều kiện RevokedAt IS NULL chặn request song song.
        var rotated = await db.RefreshTokens
            .Where(t => t.Id == current.Id && t.RevokedAt == null)
            .ExecuteUpdateAsync(s => s
                .SetProperty(t => t.RevokedAt, now)
                .SetProperty(t => t.RevokedReason, RefreshTokenRevokeReason.Rotated)
                .SetProperty(t => t.ReplacedById, successor.Id), cancellationToken);

        if (rotated == 0)
        {
            await transaction.RollbackAsync(cancellationToken);
            return IdentityErrors.InvalidRefreshToken;
        }

        db.RefreshTokens.Add(successor);
        await db.SaveChangesAsync(cancellationToken);
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

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        var revoked = await RefreshTokenRevoker.RevokeFamilyAsync(
            db, token.FamilyId, RefreshTokenRevokeReason.Reuse, now, cancellationToken);
        await db.Users
            .Where(u => u.Id == token.UserId)
            .ExecuteUpdateAsync(s => s.SetProperty(u => u.SecurityStamp, u => Guid.NewGuid()), cancellationToken);

        await transaction.CommitAsync(cancellationToken);
        sessionStore.Invalidate(token.UserId);

        logger.LogWarning(
            "Refresh token reuse detected for user {UserId}, family {FamilyId}; revoked {Count} active token(s) and all access tokens",
            token.UserId, token.FamilyId, revoked);
    }
}
