using renting_room.Application.Common.Models;
using renting_room.Application.Identity.Me;
using renting_room.Application.Identity.Members;
using renting_room.Domain.Identity;

namespace renting_room.Application.Identity;

public interface IUserQueries
{
    /// <summary>M01 ID-BR-22 / ID-BR-23 (08/11/2026): user đang hoạt động là chủ trọ hoặc quản lý khu (đọc DB mỗi lần — khóa / gỡ có hiệu lực ngay).</summary>
    Task<bool> HasOwnerLevelAccessAsync(Guid userId, CancellationToken cancellationToken);

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
