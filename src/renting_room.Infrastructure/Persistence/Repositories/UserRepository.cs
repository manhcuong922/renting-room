using Microsoft.EntityFrameworkCore;
using renting_room.Application.Identity;
using renting_room.Domain.Identity;

namespace renting_room.Infrastructure.Persistence.Repositories;

internal sealed class UserRepository(AppDbContext db) : IUserRepository
{
    public Task<User?> GetForUpdateAsync(Guid id, CancellationToken cancellationToken) =>
        db.Users.FirstOrDefaultAsync(u => u.Id == id, cancellationToken);

    public Task<User?> GetInOrganizationForUpdateAsync(Guid id, Guid? organizationId, CancellationToken cancellationToken) =>
        db.Users.FirstOrDefaultAsync(u => u.Id == id && u.OrganizationId == organizationId, cancellationToken);

    public Task<User?> GetNonAdminForUpdateAsync(Guid id, CancellationToken cancellationToken) =>
        db.Users.FirstOrDefaultAsync(u => u.Id == id && u.Role != UserRole.SystemAdmin, cancellationToken);

    public Task<bool> IsPhoneTakenAsync(string phoneNormalized, Guid? excludeId, CancellationToken cancellationToken) =>
        excludeId is { } id
            ? db.Users.AnyAsync(u => u.PhoneNormalized == phoneNormalized && u.Id != id, cancellationToken)
            : db.Users.AnyAsync(u => u.PhoneNormalized == phoneNormalized, cancellationToken);

    public Task<bool> IsEmailTakenAsync(string emailNormalized, Guid? excludeId, CancellationToken cancellationToken) =>
        excludeId is { } id
            ? db.Users.AnyAsync(u => u.EmailNormalized == emailNormalized && u.Id != id, cancellationToken)
            : db.Users.AnyAsync(u => u.EmailNormalized == emailNormalized, cancellationToken);

    public void Add(User user) => db.Users.Add(user);

    public Task RegisterFailedLoginAsync(Guid userId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var lockoutEnd = now.Add(User.LockoutDuration);

        return db.Users
            .Where(u => u.Id == userId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(
                    u => u.LockoutEnd,
                    u => u.FailedLoginCount + 1 >= User.MaxFailedLoginAttempts ? lockoutEnd : u.LockoutEnd)
                .SetProperty(
                    u => u.FailedLoginCount,
                    u => u.FailedLoginCount + 1 >= User.MaxFailedLoginAttempts ? 0 : u.FailedLoginCount + 1),
                cancellationToken);
    }

    public Task RecordSuccessfulLoginAsync(Guid userId, DateTimeOffset now, CancellationToken cancellationToken) =>
        db.Users
            .Where(u => u.Id == userId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(u => u.FailedLoginCount, 0)
                .SetProperty(u => u.LockoutEnd, (DateTimeOffset?)null)
                .SetProperty(u => u.LastLoginAt, now), cancellationToken);

    public Task UpgradePasswordHashAsync(Guid userId, string verifiedHash, string upgradedHash, CancellationToken cancellationToken) =>
        db.Users
            .Where(u => u.Id == userId && u.PasswordHash == verifiedHash)
            .ExecuteUpdateAsync(s => s.SetProperty(u => u.PasswordHash, upgradedHash), cancellationToken);

    public Task RotateSecurityStampAsync(Guid userId, CancellationToken cancellationToken) =>
        db.Users
            .Where(u => u.Id == userId)
            .ExecuteUpdateAsync(s => s.SetProperty(u => u.SecurityStamp, u => Guid.NewGuid()), cancellationToken);

    public Task RotateSecurityStampsInOrganizationAsync(Guid organizationId, CancellationToken cancellationToken) =>
        db.Users
            .Where(u => u.OrganizationId == organizationId)
            .ExecuteUpdateAsync(s => s.SetProperty(u => u.SecurityStamp, u => Guid.NewGuid()), cancellationToken);
}

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

internal sealed class LoginEventRepository(AppDbContext db) : ILoginEventRepository
{
    public void Add(LoginEvent loginEvent) => db.LoginEvents.Add(loginEvent);

    public Task<int> DeleteOlderThanAsync(DateTimeOffset cutoff, CancellationToken cancellationToken) =>
        db.LoginEvents.Where(e => e.OccurredAt < cutoff).ExecuteDeleteAsync(cancellationToken);
}
