using renting_room.Application.Common.Interfaces;
using renting_room.Domain.Identity;
using renting_room.Application.Identity;

namespace renting_room.Application.Common.Security;

/// <summary>
/// ID-BR-22 (đổi 08/11/2026): chủ trọ và quản lý khu đang hoạt động xem được số giấy tờ đầy đủ — quản lý chỉ trong khu được giao (hồ sơ ngoài phạm vi
/// không tải được — ID-BR-24). Trạng thái đọc từ DB ở mỗi request ⇒ khóa / gỡ có hiệu lực ngay.
/// </summary>
public static class SensitiveDataAccess
{
    public static async Task<bool> CanViewAsync(IUserQueries users, ICurrentUser currentUser, CancellationToken ct)
    {
        if (currentUser.Role == UserRole.OrgOwner)
            return true;
        if (currentUser.Role != UserRole.OrgManager || currentUser.UserId is not { } userId)
            return false;

        return await users.HasOwnerLevelAccessAsync(userId, ct);
    }
}
