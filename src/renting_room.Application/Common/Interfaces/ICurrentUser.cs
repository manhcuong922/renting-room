using renting_room.Domain.Identity;

namespace renting_room.Application.Common.Interfaces;

/// <summary>Người dùng của request hiện tại (đọc từ access token đã xác thực).</summary>
public interface ICurrentUser
{
    bool IsAuthenticated { get; }
    Guid? UserId { get; }
    Guid? OrganizationId { get; }
    UserRole? Role { get; }

    /// <summary>IP client của request (đã qua forwarded headers); null khi không có request (job nền, design-time).</summary>
    string? IpAddress { get; }

    /// <summary>
    /// M01 ID-BR-24 (08/11/2026): khu được giao của quản lý khu — mọi truy vấn / lệnh ghi bị giới hạn trong các khu này. null = không giới hạn
    /// (chủ trọ, job hệ thống); mảng rỗng = không thấy khu nào.
    /// </summary>
    Guid[]? PropertyIds => null;

    /// <summary>Lấy UserId; ném lỗi nếu endpoint gọi tới không yêu cầu đăng nhập (lỗi cấu hình).</summary>
    Guid RequireUserId() =>
        UserId ?? throw new InvalidOperationException("No authenticated user in the current context.");
}
