using renting_room.Application.Common.Interfaces;
using renting_room.Application.Identity.Organizations;
using renting_room.Domain.Common;
using renting_room.Domain.Identity;

namespace renting_room.Application.Identity.Members;

internal static class PasswordReset
{
    /// <param name="deliverBeforeCommit">
    /// Giao mật khẩu tạm (VD gửi email) KHI transaction còn mở: giao lỗi ⇒ trả lỗi, transaction rollback, mật khẩu cũ còn dùng được —
    /// không để người dùng bị khóa ngoài vì mật khẩu mới không tới nơi (M-14).
    /// </param>
    public static async Task<Result<TemporaryCredentials>> ResetAsync(
        IUnitOfWork unitOfWork, IRefreshTokenRepository refreshTokens, IPasswordHasher passwordHasher, IUserSessionStore sessionStore,
        TimeProvider clock, User user, CancellationToken ct, Func<TemporaryCredentials, Task<Result>>? deliverBeforeCommit = null)
    {
        var now = clock.GetUtcNow();
        var temporaryPassword = TemporaryPasswordGenerator.Generate();
        var reset = user.ResetPassword(passwordHasher.Hash(temporaryPassword), now);
        if (reset.IsFailure)
            return reset.Error!;

        await using var transaction = await unitOfWork.BeginTransactionAsync(ct);
        await unitOfWork.SaveChangesAsync(ct);
        await AccountSessions.RevokeAllAsync(refreshTokens, user.Id, now, ct);
        var credentials = new TemporaryCredentials(user.Id, user.Username, temporaryPassword, user.TempPasswordExpiresAt!.Value);
        if (deliverBeforeCommit is not null && await deliverBeforeCommit(credentials) is { IsFailure: true } failed)
            return failed.Error!;
        await transaction.CommitAsync(ct);
        sessionStore.Invalidate(user.Id);

        return credentials;
    }
}
