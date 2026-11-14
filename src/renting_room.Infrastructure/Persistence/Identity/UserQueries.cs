using Microsoft.EntityFrameworkCore;
using renting_room.Application.Common.Models;
using renting_room.Application.Identity;
using renting_room.Application.Identity.Me;
using renting_room.Application.Identity.Members;
using renting_room.Domain.Identity;

namespace renting_room.Infrastructure.Persistence.Queries;

internal sealed class UserQueries(AppDbContext db) : IUserQueries
{
    public Task<bool> HasOwnerLevelAccessAsync(Guid userId, CancellationToken cancellationToken) =>
        db.Users.AsNoTracking().AnyAsync(u => u.Id == userId && u.Status == UserStatus.Active
            && (u.Role == UserRole.OrgOwner || u.Role == UserRole.OrgManager), cancellationToken);

    public Task<User?> GetAsync(Guid id, CancellationToken cancellationToken) =>
        db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == id, cancellationToken);

    public Task<User?> FindByEmailAsync(string emailNormalized, CancellationToken cancellationToken) =>
        db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.EmailNormalized == emailNormalized, cancellationToken);

    public Task<User?> FindByPhoneAsync(string phoneNormalized, CancellationToken cancellationToken) =>
        db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.PhoneNormalized == phoneNormalized, cancellationToken);

    public Task<MeDto?> GetMeAsync(Guid userId, CancellationToken cancellationToken) =>
        db.Users
            .AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => new MeDto(
                u.Id,
                u.FullName,
                u.PhoneNormalized,
                u.EmailNormalized,
                u.Role,
                u.MustChangePassword,
                u.LastLoginAt,
                db.Organizations
                    .Where(o => o.Id == u.OrganizationId)
                    .Select(o => new MeOrganizationDto(o.Id, o.Code, o.Name))
                    .FirstOrDefault(),
                u.Role == UserRole.OrgOwner || u.Role == UserRole.OrgManager,
                u.Role == UserRole.OrgOwner || u.Role == UserRole.OrgManager))
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<MemberDto?> GetMemberAsync(Guid id, Guid? organizationId, CancellationToken cancellationToken)
    {
        var member = await db.Users.AsNoTracking()
            .Where(u => u.Id == id && u.OrganizationId == organizationId)
            .ToMemberDto()
            .FirstOrDefaultAsync(cancellationToken);
        return member is null ? null : (await WithPropertiesAsync([member], cancellationToken))[0];
    }

    public async Task<IReadOnlyList<MemberDto>> ListMembersAsync(Guid? organizationId, bool includeRemoved, CancellationToken cancellationToken) =>
        await WithPropertiesAsync(await db.Users.AsNoTracking()
            .Where(u => u.OrganizationId == organizationId && (includeRemoved || u.Status != UserStatus.Removed))
            .OrderBy(u => u.Role).ThenBy(u => u.FullName)
            .ToMemberDto()
            .ToListAsync(cancellationToken), cancellationToken);

    /// <summary>ID-BR-24: quản lý khu kèm khu được giao (rỗng nếu chưa giao); chủ trọ <c>null</c> = mọi khu.</summary>
    private async Task<IReadOnlyList<MemberDto>> WithPropertiesAsync(IReadOnlyList<MemberDto> members, CancellationToken cancellationToken)
    {
        var managerIds = members.Where(m => m.Role == UserRole.OrgManager).Select(m => m.Id).ToList();
        if (managerIds.Count == 0)
            return members;
        var assigned = await db.UserPropertyAccesses.AsNoTracking()
            .Where(a => managerIds.Contains(a.UserId))
            .OrderBy(a => a.PropertyId)
            .Select(a => new { a.UserId, a.PropertyId })
            .ToListAsync(cancellationToken);
        var byUser = assigned.ToLookup(a => a.UserId, a => a.PropertyId);
        return members.Select(m => m.Role == UserRole.OrgManager ? m with { PropertyIds = byUser[m.Id].ToList() } : m).ToList();
    }

    public Task<int> CountActiveManagersAsync(Guid organizationId, CancellationToken cancellationToken) =>
        db.Users.CountAsync(
            u => u.OrganizationId == organizationId && u.Role == UserRole.OrgManager && u.Status != UserStatus.Removed, cancellationToken);

    public async Task<IReadOnlyList<Guid>> ListIdsInOrganizationAsync(Guid organizationId, CancellationToken cancellationToken) =>
        await db.Users
            .Where(u => u.OrganizationId == organizationId)
            .Select(u => u.Id)
            .ToListAsync(cancellationToken);

    public async Task<PagedResult<LoginEventDto>> ListLoginEventsAsync(
        Guid? organizationId, ListLoginEventsQuery request, CancellationToken cancellationToken)
    {
        var query = db.LoginEvents.AsNoTracking().Where(e => e.OrganizationId == organizationId);
        if (request.UserId is { } userId)
            query = query.Where(e => e.UserId == userId);
        if (request.Success is { } success)
            query = query.Where(e => e.Success == success);

        var total = await query.CountAsync(cancellationToken);
        var items = await query.OrderByDescending(e => e.OccurredAt)
            .Skip((request.Page - 1) * request.PageSize).Take(request.PageSize)
            .Select(e => new LoginEventDto(e.Id, e.UserId, db.Users.Where(u => u.Id == e.UserId).Select(u => u.FullName).FirstOrDefault(),
                e.Success, e.Reason, e.IpAddress, e.OccurredAt))
            .ToListAsync(cancellationToken);
        return new PagedResult<LoginEventDto>(items, request.Page, request.PageSize, total);
    }
}
