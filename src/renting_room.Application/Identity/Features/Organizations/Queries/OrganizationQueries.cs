using FluentValidation;
using Mediator;
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

public sealed class ListOrganizationsHandler(IOrganizationQueries organizations)
    : IRequestHandler<ListOrganizationsQuery, PagedResult<OrganizationSummaryDto>>
{
    public async ValueTask<PagedResult<OrganizationSummaryDto>> Handle(
        ListOrganizationsQuery request, CancellationToken cancellationToken) =>
        await organizations.ListAsync(request, cancellationToken);
}

public sealed record GetOrganizationQuery(Guid Id) : IRequest<Result<OrganizationDetailDto>>;

public sealed class GetOrganizationHandler(IOrganizationQueries organizations)
    : IRequestHandler<GetOrganizationQuery, Result<OrganizationDetailDto>>
{
    public async ValueTask<Result<OrganizationDetailDto>> Handle(
        GetOrganizationQuery request, CancellationToken cancellationToken)
    {
        var organization = await organizations.GetDetailAsync(request.Id, cancellationToken);
        return organization is null ? IdentityErrors.OrganizationNotFound : organization;
    }
}
