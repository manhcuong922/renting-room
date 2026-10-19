using renting_room.Application.Common.Interfaces;
using renting_room.Domain.Identity;
using renting_room.Application.Identity;

namespace renting_room.Application.Common.Security;

/// <summary>
/// ID-BR-22: ai được xem dữ liệu nhạy cảm (số giấy tờ đầy đủ). Đọc từ DB ở mỗi request — không lấy từ token —
/// để chủ trọ thu hồi quyền là có hiệu lực ngay, không chờ token hết hạn.
/// </summary>
public static class SensitiveDataAccess
{
    public static async Task<bool> CanViewAsync(IUserQueries users, ICurrentUser currentUser, CancellationToken ct)
    {
        if (currentUser.Role == UserRole.OrgOwner)
            return true;
        if (currentUser.Role != UserRole.OrgManager || currentUser.UserId is not { } userId)
            return false;

        return await users.HasSensitiveDataAccessAsync(userId, ct);
    }
}
