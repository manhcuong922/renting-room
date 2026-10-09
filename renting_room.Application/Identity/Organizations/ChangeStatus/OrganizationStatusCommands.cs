using FluentValidation;
using Mediator;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using renting_room.Application.Common.Interfaces;
using renting_room.Application.Identity.Auth;
using renting_room.Domain.Common;
using renting_room.Domain.Identity;

namespace renting_room.Application.Identity.Organizations.ChangeStatus;

public sealed record SuspendOrganizationCommand(Guid OrganizationId, string Reason) : IRequest<Result>;

public sealed class SuspendOrganizationCommandValidator : AbstractValidator<SuspendOrganizationCommand>
{
    public SuspendOrganizationCommandValidator()
    {
        RuleFor(x => x.Reason)
            .Must(r => !string.IsNullOrWhiteSpace(r)).WithErrorCode("REQUIRED").WithMessage("Lý do tạm ngưng là bắt buộc.")
            .MaximumLength(500).WithErrorCode("MAX_LENGTH");
    }
}

/// <summary>
/// Tạm ngưng tổ chức: thu hồi mọi refresh token và đổi security stamp của toàn bộ user trong tổ chức
/// để access token đang lưu hành bị từ chối (ID-BR-08).
/// </summary>
public sealed class SuspendOrganizationHandler(
    IAppDbContext db,
    IUserSessionStore sessionStore,
    TimeProvider clock,
    ILogger<SuspendOrganizationHandler> logger)
    : IRequestHandler<SuspendOrganizationCommand, Result>
{
    public async ValueTask<Result> Handle(SuspendOrganizationCommand request, CancellationToken cancellationToken)
    {
        var organization = await db.Organizations.FirstOrDefaultAsync(o => o.Id == request.OrganizationId, cancellationToken);
        if (organization is null)
            return Result.Failure(IdentityErrors.OrganizationNotFound);

        var suspended = organization.Suspend(request.Reason);
        if (suspended.IsFailure)
            return suspended;

        var now = clock.GetUtcNow();
        await using var transaction = await db.BeginTransactionAsync(cancellationToken);

        await db.SaveChangesAsync(cancellationToken);
        await RefreshTokenRevoker.RevokeAllForOrganizationAsync(
            db, organization.Id, RefreshTokenRevokeReason.AdminAction, now, cancellationToken);
        await db.Users
            .Where(u => u.OrganizationId == organization.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(u => u.SecurityStamp, u => Guid.NewGuid()), cancellationToken);

        await transaction.CommitAsync(cancellationToken);

        var userIds = await db.Users
            .Where(u => u.OrganizationId == organization.Id)
            .Select(u => u.Id)
            .ToListAsync(cancellationToken);
        userIds.ForEach(sessionStore.Invalidate);

        logger.LogWarning("Organization {OrganizationId} suspended", organization.Id);
        return Result.Success();
    }
}

public sealed record ReactivateOrganizationCommand(Guid OrganizationId) : IRequest<Result>;

public sealed class ReactivateOrganizationHandler(IAppDbContext db, ILogger<ReactivateOrganizationHandler> logger)
    : IRequestHandler<ReactivateOrganizationCommand, Result>
{
    public async ValueTask<Result> Handle(ReactivateOrganizationCommand request, CancellationToken cancellationToken)
    {
        var organization = await db.Organizations.FirstOrDefaultAsync(o => o.Id == request.OrganizationId, cancellationToken);
        if (organization is null)
            return Result.Failure(IdentityErrors.OrganizationNotFound);

        var reactivated = organization.Reactivate();
        if (reactivated.IsFailure)
            return reactivated;

        await db.SaveChangesAsync(cancellationToken);
        logger.LogInformation("Organization {OrganizationId} reactivated", organization.Id);
        return Result.Success();
    }
}
