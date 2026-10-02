using FluentValidation;
using Mediator;
using Microsoft.EntityFrameworkCore;
using renting_room.Application.Common.Interfaces;
using renting_room.Domain.Common;
using renting_room.Domain.Identity;

namespace renting_room.Application.Identity.Auth.Logout;

/// <summary>Đăng xuất phiên hiện tại: thu hồi cả family của refresh token. Luôn thành công (không lộ token có tồn tại hay không).</summary>
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

public sealed class LogoutHandler(IAppDbContext db, ITokenService tokenService, TimeProvider clock)
    : IRequestHandler<LogoutCommand, Result>
{
    public async ValueTask<Result> Handle(LogoutCommand request, CancellationToken cancellationToken)
    {
        var tokenHash = tokenService.HashRefreshToken(request.RefreshToken);

        var familyId = await db.RefreshTokens
            .Where(t => t.TokenHash == tokenHash)
            .Select(t => (Guid?)t.FamilyId)
            .FirstOrDefaultAsync(cancellationToken);

        if (familyId is not null)
        {
            await RefreshTokenRevoker.RevokeFamilyAsync(
                db, familyId.Value, RefreshTokenRevokeReason.Logout, clock.GetUtcNow(), cancellationToken);
        }

        return Result.Success();
    }
}

/// <summary>Đăng xuất mọi thiết bị: thu hồi mọi refresh token và đổi security stamp để vô hiệu access token đang lưu hành.</summary>
public sealed record LogoutAllCommand : IRequest<Result>;

public sealed class LogoutAllHandler(
    IAppDbContext db,
    ICurrentUser currentUser,
    IUserSessionStore sessionStore,
    TimeProvider clock)
    : IRequestHandler<LogoutAllCommand, Result>
{
    public async ValueTask<Result> Handle(LogoutAllCommand request, CancellationToken cancellationToken)
    {
        var userId = currentUser.RequireUserId();
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);
        if (user is null)
            return Result.Failure(IdentityErrors.UserNotFound);

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        await RefreshTokenRevoker.RevokeAllForUserAsync(
            db, userId, RefreshTokenRevokeReason.Logout, clock.GetUtcNow(), cancellationToken);
        user.RotateSecurityStamp();
        await db.SaveChangesAsync(cancellationToken);

        await transaction.CommitAsync(cancellationToken);
        sessionStore.Invalidate(userId);

        return Result.Success();
    }
}
