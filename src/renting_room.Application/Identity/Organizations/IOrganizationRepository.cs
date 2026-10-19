using renting_room.Domain.Identity;

namespace renting_room.Application.Identity.Organizations;

/// <summary>Lấy / cất aggregate tổ chức. Không chứa luật nghiệp vụ — luật nằm trong <see cref="Organization"/>.</summary>
public interface IOrganizationRepository
{
    Task<Organization?> GetAsync(Guid id, CancellationToken cancellationToken);

    Task<bool> IsCodeTakenAsync(string code, CancellationToken cancellationToken);

    void Add(Organization organization);
}
