using renting_room.Application.Common.Models;
using renting_room.Application.Identity.Me;
using renting_room.Application.Identity.Members;
using renting_room.Domain.Identity;

namespace renting_room.Application.Identity;

public interface IUserQueries
{
    Task<bool> HasSensitiveDataAccessAsync(Guid userId, CancellationToken cancellationToken);

    Task<bool> HasWriteOffPermissionAsync(Guid userId, CancellationToken cancellationToken);

    Task<User?> GetAsync(Guid id, CancellationToken cancellationToken);

    Task<User?> FindByEmailAsync(string emailNormalized, CancellationToken cancellationToken);

    Task<User?> FindByPhoneAsync(string phoneNormalized, CancellationToken cancellationToken);

    Task<MeDto?> GetMeAsync(Guid userId, CancellationToken cancellationToken);

    Task<MemberDto?> GetMemberAsync(Guid id, Guid? organizationId, CancellationToken cancellationToken);

    /// <summary>Theo vai trò rồi họ tên.</summary>
    Task<IReadOnlyList<MemberDto>> ListMembersAsync(Guid? organizationId, bool includeRemoved, CancellationToken cancellationToken);

    Task<int> CountActiveManagersAsync(Guid organizationId, CancellationToken cancellationToken);

    Task<IReadOnlyList<Guid>> ListIdsInOrganizationAsync(Guid organizationId, CancellationToken cancellationToken);

    Task<PagedResult<LoginEventDto>> ListLoginEventsAsync(Guid? organizationId, ListLoginEventsQuery request, CancellationToken cancellationToken);
}
