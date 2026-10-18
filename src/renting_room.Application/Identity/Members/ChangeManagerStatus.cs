using Mediator;
using Microsoft.Extensions.Logging;
using renting_room.Application.Common.Interfaces;
using renting_room.Domain.Common;

namespace renting_room.Application.Identity.Members;

// Sửa / khóa / mở / cấp lại mật khẩu / gỡ (chỉ chủ trọ, chỉ phó quản lý)
public enum ManagerAction
{
    Lock,
    Unlock,
    Remove
}

public sealed record ChangeManagerStatusCommand(Guid Id, ManagerAction Action) : IRequest<Result>;

public sealed class ChangeManagerStatusHandler(
    IAppDbContext db, ICurrentUser currentUser, IUserSessionStore sessionStore, TimeProvider clock,
    ILogger<ChangeManagerStatusHandler> logger)
    : IRequestHandler<ChangeManagerStatusCommand, Result>
{
    public async ValueTask<Result> Handle(ChangeManagerStatusCommand request, CancellationToken cancellationToken)
    {
        var manager = await ManagerLookup.FindAsync(db, currentUser, request.Id, cancellationToken);
        if (manager.IsFailure)
            return Result.Failure(manager.Error!);

        var user = manager.Value!;
        var now = clock.GetUtcNow();
        var result = request.Action switch
        {
            ManagerAction.Lock => user.Lock(),
            ManagerAction.Unlock => user.Unlock(),
            _ => user.Remove(now, currentUser.RequireUserId())
        };
        if (result.IsFailure)
            return result;

        await using var transaction = await db.BeginTransactionAsync(cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        if (request.Action != ManagerAction.Unlock)
            await AccountSessions.RevokeAllAsync(db, user.Id, now, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        sessionStore.Invalidate(user.Id);

        logger.LogInformation("Manager {UserId} {Action} by {ActorId}", user.Id, request.Action, currentUser.UserId);
        return Result.Success();
    }
}
