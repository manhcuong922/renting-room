using FluentValidation;
using Mediator;
using renting_room.Application.Common.Interfaces;
using renting_room.Domain.Common;
using renting_room.Domain.Identity;

namespace renting_room.Application.Identity.Auth.Logout;

/// <summary>
/// Đăng xuất phiên hiện tại: thu hồi cả family của refresh token ⇒ access token của phiên (claim <c>sid</c>) cũng hết hiệu lực ngay.
/// Luôn thành công (không lộ token có tồn tại hay không).
/// </summary>
public sealed record LogoutCommand(string RefreshToken) : IRequest<Result>;

public sealed class LogoutCommandValidator : AbstractValidator<LogoutCommand>
{
    public LogoutCommandValidator()
    {
        RuleFor(x => x.RefreshToken)
            .NotEmpty().WithErrorCode("REQUIRED").WithMessage("Refresh token là bắt buộc.")
            .MaximumLength(200).WithErrorCode("MAX_LENGTH");
    }
}

public sealed class LogoutHandler(IRefreshTokenRepository refreshTokens, ITokenService tokenService, IUserSessionStore sessionStore, TimeProvider clock)
    : IRequestHandler<LogoutCommand, Result>
{
    public async ValueTask<Result> Handle(LogoutCommand request, CancellationToken cancellationToken)
    {
        var tokenHash = tokenService.HashRefreshToken(request.RefreshToken);

        var familyId = await refreshTokens.FindFamilyIdByHashAsync(tokenHash, cancellationToken);

        if (familyId is not null)
        {
            await refreshTokens.RevokeFamilyAsync(familyId.Value, RefreshTokenRevokeReason.Logout, clock.GetUtcNow(), cancellationToken);
            sessionStore.InvalidateSession(familyId.Value);
        }

        return Result.Success();
    }
}

/// <summary>Đăng xuất mọi thiết bị: thu hồi mọi refresh token và đổi security stamp để vô hiệu access token đang lưu hành.</summary>
public sealed record LogoutAllCommand : IRequest<Result>;

public sealed class LogoutAllHandler(
    IUserRepository users,
    IRefreshTokenRepository refreshTokens,
    IUnitOfWork unitOfWork,
    ICurrentUser currentUser,
    IUserSessionStore sessionStore,
    TimeProvider clock)
    : IRequestHandler<LogoutAllCommand, Result>
{
    public async ValueTask<Result> Handle(LogoutAllCommand request, CancellationToken cancellationToken)
    {
        var userId = currentUser.RequireUserId();
        var user = await users.GetForUpdateAsync(userId, cancellationToken);
        if (user is null)
            return Result.Failure(IdentityErrors.UserNotFound);

        await using var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken);

        await refreshTokens.RevokeAllForUserAsync(userId, RefreshTokenRevokeReason.Logout, clock.GetUtcNow(), cancellationToken);
        user.RotateSecurityStamp();
        await unitOfWork.SaveChangesAsync(cancellationToken);

        await transaction.CommitAsync(cancellationToken);
        sessionStore.Invalidate(userId);

        return Result.Success();
    }
}
