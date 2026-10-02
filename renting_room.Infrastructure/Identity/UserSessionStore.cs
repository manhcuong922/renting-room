using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using renting_room.Application.Common.Interfaces;
using renting_room.Domain.Identity;

namespace renting_room.Infrastructure.Identity;

/// <summary>
/// Phiên hợp lệ khi: user tồn tại, không bị admin khóa, tổ chức (nếu có) đang Active, và security stamp trong token
/// khớp DB. Cache trong bộ nhớ (mặc định 60 giây) — nhiều instance thì độ trễ áp dụng tối đa bằng TTL (ID-BR-08).
/// </summary>
public sealed class UserSessionStore(IAppDbContext db, IMemoryCache cache, IOptions<JwtOptions> options)
    : IUserSessionStore
{
    private sealed record SessionSnapshot(Guid SecurityStamp, bool IsActive);

    public async ValueTask<bool> IsValidAsync(Guid userId, Guid securityStamp, CancellationToken cancellationToken)
    {
        var key = CacheKey(userId);
        if (!cache.TryGetValue(key, out SessionSnapshot? snapshot) || snapshot is null)
        {
            snapshot = await LoadAsync(userId, cancellationToken);
            var ttl = TimeSpan.FromSeconds(options.Value.SessionCacheSeconds);
            if (ttl > TimeSpan.Zero)
                cache.Set(key, snapshot, ttl);
        }

        return snapshot.IsActive && snapshot.SecurityStamp == securityStamp;
    }

    public void Invalidate(Guid userId) => cache.Remove(CacheKey(userId));

    private async Task<SessionSnapshot> LoadAsync(Guid userId, CancellationToken cancellationToken)
    {
        var state = await db.Users
            .AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => new
            {
                u.SecurityStamp,
                u.Status,
                IsOrganizationActive = u.OrganizationId == null
                    || db.Organizations.Any(o => o.Id == u.OrganizationId && o.Status == OrganizationStatus.Active)
            })
            .FirstOrDefaultAsync(cancellationToken);

        return state is null
            ? new SessionSnapshot(Guid.Empty, IsActive: false)
            : new SessionSnapshot(state.SecurityStamp, state.Status == UserStatus.Active && state.IsOrganizationActive);
    }

    private static string CacheKey(Guid userId) => $"session:{userId:N}";
}
