using renting_room.Application.Common.Models;
using renting_room.Application.Identity.Organizations.Queries;
using renting_room.Domain.Identity;
using renting_room.Domain.Properties;

namespace renting_room.Application.Identity.Organizations;

public interface IOrganizationQueries
{
    /// <summary>Tổ chức (chỉ đọc) — không dùng để sửa.</summary>
    Task<Organization?> GetAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>null ⇒ không có tổ chức này.</summary>
    Task<OrganizationStatus?> GetStatusAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>SystemAdmin: mọi tổ chức, mới tạo trước; kèm số khu / phòng chưa ngừng dùng.</summary>
    Task<PagedResult<OrganizationSummaryDto>> ListAsync(ListOrganizationsQuery request, CancellationToken cancellationToken);

    Task<OrganizationDetailDto?> GetDetailAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>PR-BR-17: bên cho thuê mặc định = thông tin chủ trọ; null = chưa khai.</summary>
    Task<LessorDetails?> GetDefaultLessorAsync(Guid organizationId, CancellationToken cancellationToken);
}
