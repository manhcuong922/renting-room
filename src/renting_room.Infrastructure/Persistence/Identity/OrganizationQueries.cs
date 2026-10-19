using Microsoft.EntityFrameworkCore;
using renting_room.Application.Common.Models;
using renting_room.Application.Identity.Organizations;
using renting_room.Application.Identity.Organizations.Queries;
using renting_room.Domain.Identity;
using renting_room.Domain.Properties;

namespace renting_room.Infrastructure.Persistence.Queries;

internal sealed class OrganizationQueries(AppDbContext db) : IOrganizationQueries
{
    public Task<Organization?> GetAsync(Guid id, CancellationToken cancellationToken) =>
        db.Organizations.AsNoTracking().FirstOrDefaultAsync(o => o.Id == id, cancellationToken);

    public Task<OrganizationStatus?> GetStatusAsync(Guid id, CancellationToken cancellationToken) =>
        db.Organizations.Where(o => o.Id == id).Select(o => (OrganizationStatus?)o.Status).FirstOrDefaultAsync(cancellationToken);

    public Task<LessorDetails?> GetDefaultLessorAsync(Guid organizationId, CancellationToken cancellationToken) =>
        db.Organizations.AsNoTracking().Where(o => o.Id == organizationId).Select(o => o.DefaultLessor).FirstOrDefaultAsync(cancellationToken);

    public async Task<PagedResult<OrganizationSummaryDto>> ListAsync(ListOrganizationsQuery request, CancellationToken cancellationToken)
    {
        var query = db.Organizations.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var term = request.Search.Trim().ToLower();
            query = query.Where(o => o.Name.ToLower().Contains(term) || o.Code.ToLower().Contains(term));
        }

        if (request.Status is { } status)
            query = query.Where(o => o.Status == status);

        var totalCount = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderByDescending(o => o.CreatedAt)
            .ThenBy(o => o.Id)
            .Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize)
            // ID-UC-02: số khu / phòng chưa ngừng dùng — đếm on-demand. SystemAdmin không thuộc tổ chức nào nên phải bỏ bộ lọc theo tổ chức.
            .Select(o => new OrganizationSummaryDto(
                o.Id, o.Code, o.Name, o.Status, o.ContactName, o.ContactPhone, o.CreatedAt,
                db.Properties.IgnoreQueryFilters().Count(p => p.OrganizationId == o.Id && p.ArchivedAt == null),
                db.Rooms.IgnoreQueryFilters().Count(r => r.OrganizationId == o.Id && r.ArchivedAt == null)))
            .ToListAsync(cancellationToken);

        return new PagedResult<OrganizationSummaryDto>(items, request.Page, request.PageSize, totalCount);
    }

    public async Task<OrganizationDetailDto?> GetDetailAsync(Guid id, CancellationToken cancellationToken)
    {
        var organization = await db.Organizations
            .AsNoTracking()
            .FirstOrDefaultAsync(o => o.Id == id, cancellationToken);

        if (organization is null)
            return null;

        var users = await db.Users
            .AsNoTracking()
            .Where(u => u.OrganizationId == organization.Id)
            .OrderBy(u => u.Role).ThenBy(u => u.FullName)
            .Select(u => new OrganizationUserDto(
                u.Id, u.FullName, u.PhoneNormalized, u.EmailNormalized, u.Role, u.Status,
                u.MustChangePassword, u.LastLoginAt))
            .ToListAsync(cancellationToken);

        return new OrganizationDetailDto(
            organization.Id,
            organization.Code,
            organization.Name,
            organization.Status,
            organization.SuspendedReason,
            organization.ContactName,
            organization.ContactPhone,
            organization.ContactEmail,
            organization.TaxCode,
            organization.Note,
            organization.CreatedAt,
            organization.Version.ToString(),
            users);
    }
}
