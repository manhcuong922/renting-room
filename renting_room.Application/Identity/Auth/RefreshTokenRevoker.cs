using Microsoft.EntityFrameworkCore;
using renting_room.Application.Common.Interfaces;
using renting_room.Domain.Identity;

namespace renting_room.Application.Identity.Auth;

/// <summary>Thu hồi refresh token bằng lệnh UPDATE nguyên tử (không load entity), an toàn khi chạy song song.</summary>
internal static class RefreshTokenRevoker
{
    public static Task<int> RevokeFamilyAsync(
        IAppDbContext db, Guid familyId, RefreshTokenRevokeReason reason, DateTimeOffset now, CancellationToken ct) =>
        db.RefreshTokens
            .Where(t => t.FamilyId == familyId && t.RevokedAt == null)
            .ExecuteUpdateAsync(s => s
                .SetProperty(t => t.RevokedAt, now)
                .SetProperty(t => t.RevokedReason, reason), ct);

    public static Task<int> RevokeAllForUserAsync(
        IAppDbContext db, Guid userId, RefreshTokenRevokeReason reason, DateTimeOffset now, CancellationToken ct) =>
        db.RefreshTokens
            .Where(t => t.UserId == userId && t.RevokedAt == null)
            .ExecuteUpdateAsync(s => s
                .SetProperty(t => t.RevokedAt, now)
                .SetProperty(t => t.RevokedReason, reason), ct);

    public static Task<int> RevokeAllForOrganizationAsync(
        IAppDbContext db, Guid organizationId, RefreshTokenRevokeReason reason, DateTimeOffset now, CancellationToken ct) =>
        db.RefreshTokens
            .Where(t => t.RevokedAt == null
                && db.Users.Any(u => u.Id == t.UserId && u.OrganizationId == organizationId))
            .ExecuteUpdateAsync(s => s
                .SetProperty(t => t.RevokedAt, now)
                .SetProperty(t => t.RevokedReason, reason), ct);
}
