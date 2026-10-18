using Mediator;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using renting_room.Application.Common.Interfaces;
using renting_room.Domain.Common;
using renting_room.Domain.Identity;

namespace renting_room.Application.Identity.Auth.ChangePassword;

public sealed class ChangePasswordHandler(
    IAppDbContext db,
    ICurrentUser currentUser,
    IPasswordHasher passwordHasher,
    AuthTokenIssuer tokenIssuer,
    IUserSessionStore sessionStore,
    TimeProvider clock,
    ILogger<ChangePasswordHandler> logger)
    : IRequestHandler<ChangePasswordCommand, Result<AuthTokens>>
{
    public async ValueTask<Result<AuthTokens>> Handle(ChangePasswordCommand request, CancellationToken cancellationToken)
    {
        var userId = currentUser.RequireUserId();
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);
        if (user is null)
            return IdentityErrors.UserNotFound;

        var now = clock.GetUtcNow();

        // Access token bị đánh cắp không được dùng để dò mật khẩu hiện tại: chung bộ đếm khóa với đăng nhập.
        if (user.IsLockedOut(now))
            return IdentityErrors.AccountLocked;

        if (passwordHasher.Verify(user.PasswordHash, request.CurrentPassword) == PasswordCheckResult.Failed)
        {
            await FailedLoginTracker.RegisterFailureAsync(db, user.Id, now, cancellationToken);
            logger.LogWarning("Change password failed: wrong current password for user {UserId}", user.Id);
            return IdentityErrors.InvalidCurrentPassword;
        }

        if (request.NewPassword == request.CurrentPassword)
            return IdentityErrors.PasswordReused;

        if (ContainsUsername(request.NewPassword, user))
            return IdentityErrors.PasswordContainsUsername;

        user.ChangePassword(passwordHasher.Hash(request.NewPassword), now);

        await using var transaction = await db.BeginTransactionAsync(cancellationToken);

        await RefreshTokenRevoker.RevokeAllForUserAsync(
            db, user.Id, RefreshTokenRevokeReason.PasswordChanged, now, cancellationToken);

        var (tokens, refreshToken) = tokenIssuer.IssueNewSession(user, now, request.IpAddress);
        db.RefreshTokens.Add(refreshToken);
        await db.SaveChangesAsync(cancellationToken);

        await transaction.CommitAsync(cancellationToken);
        sessionStore.Invalidate(user.Id);

        logger.LogInformation("User {UserId} changed password; all other sessions revoked", user.Id);
        return tokens;
    }

    private static bool ContainsUsername(string password, User user) =>
        (user.PhoneNormalized is { } phone && password.Contains(phone, StringComparison.OrdinalIgnoreCase))
        || (user.EmailNormalized is { } email && password.Contains(email, StringComparison.OrdinalIgnoreCase));
}
