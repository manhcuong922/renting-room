using Microsoft.EntityFrameworkCore;
using renting_room.Application.Identity;
using renting_room.Domain.Identity;

namespace renting_room.Infrastructure.Persistence.Repositories;

internal sealed class RefreshTokenRepository(AppDbContext db) : IRefreshTokenRepository
{
    public Task<RefreshToken?> FindByHashAsync(string tokenHash, CancellationToken cancellationToken) =>
        db.RefreshTokens.AsNoTracking().FirstOrDefaultAsync(t => t.TokenHash == tokenHash, cancellationToken);

    public Task<Guid?> FindFamilyIdByHashAsync(string tokenHash, CancellationToken cancellationToken) =>
        db.RefreshTokens.Where(t => t.TokenHash == tokenHash).Select(t => (Guid?)t.FamilyId).FirstOrDefaultAsync(cancellationToken);

    public void Add(RefreshToken token) => db.RefreshTokens.Add(token);

    public Task<int> RevokeFamilyAsync(Guid familyId, RefreshTokenRevokeReason reason, DateTimeOffset now, CancellationToken cancellationToken) =>
        db.RefreshTokens
            .Where(t => t.FamilyId == familyId && t.RevokedAt == null)
            .ExecuteUpdateAsync(s => s
                .SetProperty(t => t.RevokedAt, now)
                .SetProperty(t => t.RevokedReason, reason), cancellationToken);

    public Task<int> RevokeAllForUserAsync(Guid userId, RefreshTokenRevokeReason reason, DateTimeOffset now, CancellationToken cancellationToken) =>
        db.RefreshTokens
            .Where(t => t.UserId == userId && t.RevokedAt == null)
            .ExecuteUpdateAsync(s => s
                .SetProperty(t => t.RevokedAt, now)
                .SetProperty(t => t.RevokedReason, reason), cancellationToken);

    public Task<int> RevokeAllForOrganizationAsync(
        Guid organizationId, RefreshTokenRevokeReason reason, DateTimeOffset now, CancellationToken cancellationToken) =>
        db.RefreshTokens
            .Where(t => t.RevokedAt == null
                && db.Users.Any(u => u.Id == t.UserId && u.OrganizationId == organizationId))
            .ExecuteUpdateAsync(s => s
                .SetProperty(t => t.RevokedAt, now)
                .SetProperty(t => t.RevokedReason, reason), cancellationToken);

    public Task<int> MarkRotatedAsync(Guid id, Guid successorId, DateTimeOffset now, CancellationToken cancellationToken) =>
        db.RefreshTokens
            .Where(t => t.Id == id && t.RevokedAt == null)
            .ExecuteUpdateAsync(s => s
                .SetProperty(t => t.RevokedAt, now)
                .SetProperty(t => t.RevokedReason, RefreshTokenRevokeReason.Rotated)
                .SetProperty(t => t.ReplacedById, successorId), cancellationToken);

    public Task<int> DeleteExpiredBeforeAsync(DateTimeOffset cutoff, CancellationToken cancellationToken) =>
        db.RefreshTokens.Where(t => t.ExpiresAt < cutoff).ExecuteDeleteAsync(cancellationToken);
}
