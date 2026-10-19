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
