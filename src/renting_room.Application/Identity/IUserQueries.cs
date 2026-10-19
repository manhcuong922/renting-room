using renting_room.Application.Common.Models;
using renting_room.Application.Identity.Me;
using renting_room.Application.Identity.Members;
using renting_room.Domain.Identity;

namespace renting_room.Application.Identity;

public interface IUserQueries
{
    /// <summary>Tài khoản đang hoạt động và được chủ trọ cấp quyền xem dữ liệu nhạy cảm (ID-BR-22).</summary>
    Task<bool> HasSensitiveDataAccessAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>Tài khoản đang hoạt động và được chủ trọ bật quyền bỏ nợ (PM-BR-16).</summary>
    Task<bool> HasWriteOffPermissionAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>Tài khoản (chỉ đọc) — không dùng để sửa.</summary>
    Task<User?> GetAsync(Guid id, CancellationToken cancellationToken);

    Task<User?> FindByEmailAsync(string emailNormalized, CancellationToken cancellationToken);

    Task<User?> FindByPhoneAsync(string phoneNormalized, CancellationToken cancellationToken);

    Task<MeDto?> GetMeAsync(Guid userId, CancellationToken cancellationToken);

    Task<MemberDto?> GetMemberAsync(Guid id, Guid? organizationId, CancellationToken cancellationToken);

    /// <summary>Theo vai trò rồi họ tên.</summary>
    Task<IReadOnlyList<MemberDto>> ListMembersAsync(Guid? organizationId, bool includeRemoved, CancellationToken cancellationToken);

    /// <summary>Phó quản lý chưa bị gỡ của tổ chức (ID-BR-15).</summary>
    Task<int> CountActiveManagersAsync(Guid organizationId, CancellationToken cancellationToken);

    Task<IReadOnlyList<Guid>> ListIdsInOrganizationAsync(Guid organizationId, CancellationToken cancellationToken);

    /// <summary>Mới nhất trước.</summary>
    Task<PagedResult<LoginEventDto>> ListLoginEventsAsync(Guid? organizationId, ListLoginEventsQuery request, CancellationToken cancellationToken);
}
