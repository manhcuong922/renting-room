using Mediator;
using renting_room.Application.Common.Interfaces;
using renting_room.Domain.Common;

namespace renting_room.Application.Identity.Members;

public sealed record ResetManagerPasswordCommand(Guid Id) : IRequest<Result<TemporaryCredentials>>;

public sealed class ResetManagerPasswordHandler(
    IUserRepository users, IRefreshTokenRepository refreshTokens, IUnitOfWork unitOfWork, ICurrentUser currentUser, IPasswordHasher passwordHasher,
    IUserSessionStore sessionStore, TimeProvider clock)
    : IRequestHandler<ResetManagerPasswordCommand, Result<TemporaryCredentials>>
{
    public async ValueTask<Result<TemporaryCredentials>> Handle(ResetManagerPasswordCommand request, CancellationToken cancellationToken)
    {
        var manager = await ManagerLookup.FindAsync(users, currentUser, request.Id, cancellationToken);
        if (manager.IsFailure)
            return manager.Error!;

        return await PasswordReset.ResetAsync(unitOfWork, refreshTokens, passwordHasher, sessionStore, clock, manager.Value!, cancellationToken);
    }
}
