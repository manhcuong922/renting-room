using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using renting_room.Application.Identity;
using renting_room.Domain.Identity;
using renting_room.Infrastructure.Identity;

namespace renting_room.Infrastructure.Persistence.Repositories;

internal sealed class PropertyAccessRepository(AppDbContext db) : IPropertyAccessRepository
{
    public async Task<IReadOnlyDictionary<Guid, IReadOnlyList<Guid>>> ListAsync(IReadOnlyCollection<Guid> userIds, CancellationToken cancellationToken) =>
        (await db.UserPropertyAccesses.AsNoTracking().Where(a => userIds.Contains(a.UserId)).Select(a => new { a.UserId, a.PropertyId }).ToListAsync(cancellationToken))
        .GroupBy(a => a.UserId)
        .ToDictionary(g => g.Key, g => (IReadOnlyList<Guid>)g.Select(a => a.PropertyId).Order().ToList());

    public async Task ReplaceAsync(Guid userId, IReadOnlyCollection<Guid> propertyIds, CancellationToken cancellationToken)
    {
        var current = await db.UserPropertyAccesses.Where(a => a.UserId == userId).ToListAsync(cancellationToken);
        db.UserPropertyAccesses.RemoveRange(current.Where(a => !propertyIds.Contains(a.PropertyId)));
        foreach (var propertyId in propertyIds.Where(id => current.All(a => a.PropertyId != id)))
            db.UserPropertyAccesses.Add(UserPropertyAccess.Grant(userId, propertyId));
    }

    public async Task<IReadOnlyList<Guid>> ListActivePropertyIdsAsync(IReadOnlyCollection<Guid>? propertyIds, CancellationToken cancellationToken) =>
        await db.Properties.AsNoTracking()
            .Where(p => p.ArchivedAt == null && (propertyIds == null || propertyIds.Contains(p.Id)))
            .OrderBy(p => p.Id).Select(p => p.Id)
            .ToListAsync(cancellationToken);
}

/// <summary>
/// ID-BR-24: khu được giao của quản lý khu, đọc khi xác thực access token (chưa có org trong HttpContext.User ⇒ bỏ global filter, lọc theo user viết tay).
/// Cache cùng thời gian với phiên (<c>Jwt:SessionCacheSeconds</c>); chủ trọ đổi giao khu ⇒ <see cref="Invalidate"/> ⇒ request kế tiếp đọc lại.
/// </summary>
public sealed class PropertyScopeStore(AppDbContext db, IMemoryCache cache, IOptions<JwtOptions> options) : IPropertyScopeStore
{
    public async ValueTask<Guid[]?> GetAsync(Guid userId, CancellationToken cancellationToken)
    {
        var key = CacheKey(userId);
        if (cache.TryGetValue(key, out Guid[]? ids) && ids is not null)
            return ids;

        var isManager = await db.Users.AsNoTracking().AnyAsync(u => u.Id == userId && u.Role == UserRole.OrgManager, cancellationToken);
        ids = isManager
            ? await db.UserPropertyAccesses.IgnoreQueryFilters().AsNoTracking().Where(a => a.UserId == userId)
                .Select(a => a.PropertyId).OrderBy(id => id).ToArrayAsync(cancellationToken)
            : null;
        var ttl = TimeSpan.FromSeconds(options.Value.SessionCacheSeconds);
        if (ids is not null && ttl > TimeSpan.Zero)
            cache.Set(key, ids, ttl);
        return ids;
    }

    public void Invalidate(Guid userId) => cache.Remove(CacheKey(userId));

    private static string CacheKey(Guid userId) => $"property-scope:{userId:N}";
}
