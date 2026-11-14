using renting_room.Application.Common.Interfaces;
using renting_room.Domain.Common;
using renting_room.Domain.Identity;

namespace renting_room.Application.Identity;

/// <summary>
/// Nhập lại mật khẩu đăng nhập trước thao tác nhạy cảm (đổi SĐT / email đăng nhập — ID-UC-12; đổi tài khoản ngân hàng nhận tiền — H-06):
/// phiên bỏ quên / access token bị lộ không đủ để làm. Sai mật khẩu tính vào bộ đếm khóa tài khoản như đăng nhập sai ⇒ không dò được mật khẩu.
/// </summary>
public sealed class PasswordConfirmation(IUserRepository users, ICurrentUser currentUser, IPasswordHasher passwordHasher, TimeProvider clock)
{
    public async Task<Result> ConfirmCurrentUserAsync(string? password, CancellationToken ct)
    {
        var user = await users.GetForUpdateAsync(currentUser.RequireUserId(), ct);
        return user is null ? Result.Failure(IdentityErrors.UserNotFound) : await ConfirmAsync(user, password, ct);
    }

    public async Task<Result> ConfirmAsync(User user, string? password, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(password))
            return Result.Failure(IdentityErrors.CurrentPasswordRequired);
        var now = clock.GetUtcNow();
        if (user.IsLockedOut(now))
            return Result.Failure(IdentityErrors.AccountLocked);
        if (passwordHasher.Verify(user.PasswordHash, password) != PasswordCheckResult.Failed)
            return Result.Success();

        await users.RegisterFailedLoginAsync(user.Id, now, ct);
        return Result.Failure(IdentityErrors.InvalidCurrentPassword);
    }
}
