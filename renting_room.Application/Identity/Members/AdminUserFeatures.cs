using Mediator;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using renting_room.Application.Common.Interfaces;
using renting_room.Domain.Common;
using renting_room.Domain.Identity;

namespace renting_room.Application.Identity.Members;

/// <summary>
/// SystemAdmin chỉ quản lý TÀI KHOẢN (cấp lại mật khẩu, khóa, mở khóa) — không xem dữ liệu trọ (ID-BR-11).
/// Không tác động được tài khoản SystemAdmin khác qua các lệnh này.
/// </summary>
public sealed record AdminResetPasswordCommand(Guid UserId) : IRequest<Result<TemporaryCredentials>>;

public sealed class AdminResetPasswordHandler(
    IAppDbContext db, IPasswordHasher passwordHasher, IUserSessionStore sessionStore, TimeProvider clock,
    ILogger<AdminResetPasswordHandler> logger)
    : IRequestHandler<AdminResetPasswordCommand, Result<TemporaryCredentials>>
{
    public async ValueTask<Result<TemporaryCredentials>> Handle(AdminResetPasswordCommand request, CancellationToken cancellationToken)
    {
        var user = await AdminUserLookup.FindAsync(db, request.UserId, cancellationToken);
        if (user is null)
            return IdentityErrors.UserNotFound;

        var result = await PasswordReset.ResetAsync(db, passwordHasher, sessionStore, clock, user, cancellationToken);
        if (result.IsSuccess)
            logger.LogInformation("Admin reset password of user {UserId}", user.Id);
        return result;
    }
}

public sealed record AdminSetUserLockCommand(Guid UserId, bool Locked) : IRequest<Result>;

public sealed class AdminSetUserLockHandler(
    IAppDbContext db, IUserSessionStore sessionStore, TimeProvider clock, ILogger<AdminSetUserLockHandler> logger)
    : IRequestHandler<AdminSetUserLockCommand, Result>
{
    public async ValueTask<Result> Handle(AdminSetUserLockCommand request, CancellationToken cancellationToken)
    {
        var user = await AdminUserLookup.FindAsync(db, request.UserId, cancellationToken);
        if (user is null)
            return Result.Failure(IdentityErrors.UserNotFound);

        // ID-BR-04: chủ trọ là chủ duy nhất của tổ chức — muốn chặn thì tạm ngưng tổ chức.
        if (request.Locked && user.Role == UserRole.OrgOwner)
            return Result.Failure(IdentityErrors.CannotLockOwner);

        var result = request.Locked ? user.Lock() : user.Unlock();
        if (result.IsFailure)
            return result;

        var now = clock.GetUtcNow();
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        if (request.Locked)
            await AccountSessions.RevokeAllAsync(db, user.Id, now, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        sessionStore.Invalidate(user.Id);

        logger.LogInformation("Admin {Action} user {UserId}", request.Locked ? "locked" : "unlocked", user.Id);
        return Result.Success();
    }
}

internal static class AdminUserLookup
{
    public static Task<User?> FindAsync(IAppDbContext db, Guid userId, CancellationToken ct) =>
        db.Users.FirstOrDefaultAsync(u => u.Id == userId && u.Role != UserRole.SystemAdmin, ct);
}
