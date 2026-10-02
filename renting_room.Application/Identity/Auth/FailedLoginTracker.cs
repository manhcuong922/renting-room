using Microsoft.EntityFrameworkCore;
using renting_room.Application.Common.Interfaces;
using renting_room.Domain.Identity;

namespace renting_room.Application.Identity.Auth;

/// <summary>
/// Đếm lần nhập sai mật khẩu (đăng nhập và đổi mật khẩu dùng chung bộ đếm — ID-BR-06).
/// UPDATE nguyên tử (không đọc-sửa-ghi) để nhiều request sai song song không làm lệch bộ đếm.
/// Đủ ngưỡng → khóa tạm và đặt lại bộ đếm.
/// </summary>
internal static class FailedLoginTracker
{
    public static Task RegisterFailureAsync(IAppDbContext db, Guid userId, DateTimeOffset now, CancellationToken ct)
    {
        var lockoutEnd = now.Add(User.LockoutDuration);

        return db.Users
            .Where(u => u.Id == userId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(
                    u => u.LockoutEnd,
                    u => u.FailedLoginCount + 1 >= User.MaxFailedLoginAttempts ? lockoutEnd : u.LockoutEnd)
                .SetProperty(
                    u => u.FailedLoginCount,
                    u => u.FailedLoginCount + 1 >= User.MaxFailedLoginAttempts ? 0 : u.FailedLoginCount + 1),
                ct);
    }
}
