using FluentValidation;
using Mediator;
using Microsoft.Extensions.Logging;
using renting_room.Application.Common.Interfaces;
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
    IOrganizationRepository organizations,
    IUserRepository users,
    IUserQueries userQueries,
    IRefreshTokenRepository refreshTokens,
    IUnitOfWork unitOfWork,
    IUserSessionStore sessionStore,
    TimeProvider clock,
    ILogger<SuspendOrganizationHandler> logger)
    : IRequestHandler<SuspendOrganizationCommand, Result>
{
    public async ValueTask<Result> Handle(SuspendOrganizationCommand request, CancellationToken cancellationToken)
    {
        var organization = await organizations.GetAsync(request.OrganizationId, cancellationToken);
        if (organization is null)
            return Result.Failure(IdentityErrors.OrganizationNotFound);

        var suspended = organization.Suspend(request.Reason);
        if (suspended.IsFailure)
            return suspended;

        var now = clock.GetUtcNow();
        await using var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken);

        await unitOfWork.SaveChangesAsync(cancellationToken);
        await refreshTokens.RevokeAllForOrganizationAsync(organization.Id, RefreshTokenRevokeReason.AdminAction, now, cancellationToken);
        await users.RotateSecurityStampsInOrganizationAsync(organization.Id, cancellationToken);

        await transaction.CommitAsync(cancellationToken);

        var userIds = await userQueries.ListIdsInOrganizationAsync(organization.Id, cancellationToken);
        foreach (var userId in userIds)
            sessionStore.Invalidate(userId);

        logger.LogWarning("Organization {OrganizationId} suspended", organization.Id);
        return Result.Success();
    }
}

public sealed record ReactivateOrganizationCommand(Guid OrganizationId) : IRequest<Result>;

public sealed class ReactivateOrganizationHandler(
    IOrganizationRepository organizations, IUnitOfWork unitOfWork, ILogger<ReactivateOrganizationHandler> logger)
    : IRequestHandler<ReactivateOrganizationCommand, Result>
{
    public async ValueTask<Result> Handle(ReactivateOrganizationCommand request, CancellationToken cancellationToken)
    {
        var organization = await organizations.GetAsync(request.OrganizationId, cancellationToken);
        if (organization is null)
            return Result.Failure(IdentityErrors.OrganizationNotFound);

        var reactivated = organization.Reactivate();
        if (reactivated.IsFailure)
            return reactivated;

        await unitOfWork.SaveChangesAsync(cancellationToken);
        logger.LogInformation("Organization {OrganizationId} reactivated", organization.Id);
        return Result.Success();
    }
}
