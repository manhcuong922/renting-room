using renting_room.Domain.Identity;

namespace renting_room.Application.Common.Interfaces;

/// <summary>Người dùng của request hiện tại (đọc từ access token đã xác thực).</summary>
public interface ICurrentUser
{
    bool IsAuthenticated { get; }
    Guid? UserId { get; }
    Guid? OrganizationId { get; }
    UserRole? Role { get; }

    /// <summary>Lấy UserId; ném lỗi nếu endpoint gọi tới không yêu cầu đăng nhập (lỗi cấu hình).</summary>
    Guid RequireUserId() =>
        UserId ?? throw new InvalidOperationException("No authenticated user in the current context.");
}
