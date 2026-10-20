using renting_room.Application.Common.Models;
using renting_room.Application.Identity.Organizations.Queries;
using renting_room.Domain.Identity;
using renting_room.Domain.Properties;

namespace renting_room.Application.Identity.Organizations;

public interface IOrganizationQueries
{
    Task<Organization?> GetAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>null ⇒ không có tổ chức này.</summary>
    Task<OrganizationStatus?> GetStatusAsync(Guid id, CancellationToken cancellationToken);

    Task<PagedResult<OrganizationSummaryDto>> ListAsync(ListOrganizationsQuery request, CancellationToken cancellationToken);

    Task<OrganizationDetailDto?> GetDetailAsync(Guid id, CancellationToken cancellationToken);

    Task<LessorDetails?> GetDefaultLessorAsync(Guid organizationId, CancellationToken cancellationToken);
}
