using FluentValidation;
using Mediator;
using Microsoft.EntityFrameworkCore;
using renting_room.Application.Common.Interfaces;
using renting_room.Application.Common.Models;
using renting_room.Domain.Common;
using renting_room.Domain.Identity;

namespace renting_room.Application.Identity.Organizations.Queries;

public sealed record OrganizationSummaryDto(
    Guid Id,
    string Code,
    string Name,
    OrganizationStatus Status,
    string? ContactName,
    string? ContactPhone,
    DateTimeOffset CreatedAt,
    int PropertyCount = 0,
    int RoomCount = 0);

public sealed record OrganizationUserDto(
    Guid Id,
    string FullName,
    string? Phone,
    string? Email,
    UserRole Role,
    UserStatus Status,
    bool MustChangePassword,
    DateTimeOffset? LastLoginAt);

public sealed record OrganizationDetailDto(
    Guid Id,
    string Code,
    string Name,
    OrganizationStatus Status,
    string? SuspendedReason,
    string? ContactName,
    string? ContactPhone,
    string? ContactEmail,
    string? TaxCode,
    string? Note,
    DateTimeOffset CreatedAt,
    string Version,
    IReadOnlyList<OrganizationUserDto> Users);

public sealed record ListOrganizationsQuery(
    string? Search,
    OrganizationStatus? Status,
    int Page = 1,
    int PageSize = Paging.DefaultPageSize) : IRequest<PagedResult<OrganizationSummaryDto>>;

public sealed class ListOrganizationsQueryValidator : AbstractValidator<ListOrganizationsQuery>
{
    public ListOrganizationsQueryValidator()
    {
        RuleFor(x => x.Page).GreaterThanOrEqualTo(1).WithErrorCode("OUT_OF_RANGE");
        RuleFor(x => x.PageSize).InclusiveBetween(1, Paging.MaxPageSize).WithErrorCode("OUT_OF_RANGE");
        RuleFor(x => x.Search).MaximumLength(100).WithErrorCode("MAX_LENGTH");
    }
}

public sealed class ListOrganizationsHandler(IAppDbContext db)
    : IRequestHandler<ListOrganizationsQuery, PagedResult<OrganizationSummaryDto>>
{
    public async ValueTask<PagedResult<OrganizationSummaryDto>> Handle(
        ListOrganizationsQuery request, CancellationToken cancellationToken)
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
}

public sealed record GetOrganizationQuery(Guid Id) : IRequest<Result<OrganizationDetailDto>>;

public sealed class GetOrganizationHandler(IAppDbContext db)
    : IRequestHandler<GetOrganizationQuery, Result<OrganizationDetailDto>>
{
    public async ValueTask<Result<OrganizationDetailDto>> Handle(
        GetOrganizationQuery request, CancellationToken cancellationToken)
    {
        var organization = await db.Organizations
            .AsNoTracking()
            .FirstOrDefaultAsync(o => o.Id == request.Id, cancellationToken);

        if (organization is null)
            return IdentityErrors.OrganizationNotFound;

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
