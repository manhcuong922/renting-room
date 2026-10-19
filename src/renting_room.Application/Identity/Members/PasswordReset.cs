using renting_room.Application.Common.Interfaces;
using renting_room.Application.Identity.Organizations;
using renting_room.Domain.Common;
using renting_room.Domain.Identity;

namespace renting_room.Application.Identity.Members;

internal static class PasswordReset
{
    public static async Task<Result<TemporaryCredentials>> ResetAsync(
        IUnitOfWork unitOfWork, IRefreshTokenRepository refreshTokens, IPasswordHasher passwordHasher, IUserSessionStore sessionStore,
        TimeProvider clock, User user, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var temporaryPassword = TemporaryPasswordGenerator.Generate();
        var reset = user.ResetPassword(passwordHasher.Hash(temporaryPassword), now);
        if (reset.IsFailure)
            return reset.Error!;

        await using var transaction = await unitOfWork.BeginTransactionAsync(ct);
        await unitOfWork.SaveChangesAsync(ct);
        await AccountSessions.RevokeAllAsync(refreshTokens, user.Id, now, ct);
        await transaction.CommitAsync(ct);
        sessionStore.Invalidate(user.Id);

        return new TemporaryCredentials(user.Id, user.Username, temporaryPassword, user.TempPasswordExpiresAt!.Value);
    }
}
