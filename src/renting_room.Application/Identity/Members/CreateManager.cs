using FluentValidation;
using Mediator;
using Microsoft.Extensions.Logging;
using renting_room.Application.Common.Interfaces;
using renting_room.Application.Common.Validation;
using renting_room.Application.Identity.Organizations;
using renting_room.Domain.Common;
using renting_room.Domain.Identity;

namespace renting_room.Application.Identity.Members;

public sealed record CreateManagerCommand(string FullName, string? Phone, string? Email) : IRequest<Result<TemporaryCredentials>>;

public sealed class CreateManagerCommandValidator : AbstractValidator<CreateManagerCommand>
{
    public CreateManagerCommandValidator()
    {
        RuleFor(x => x.FullName).FullName();
        RuleFor(x => x.Phone).VietnamPhone();
        RuleFor(x => x.Email).ValidEmail();
        RuleFor(x => x)
            .Must(x => !string.IsNullOrWhiteSpace(x.Phone) || !string.IsNullOrWhiteSpace(x.Email))
            .OverridePropertyName("phone").WithErrorCode("USERNAME_REQUIRED")
            .WithMessage("Phó quản lý phải có số điện thoại hoặc email để đăng nhập.");
    }
}

public sealed class CreateManagerHandler(
    IUserRepository users,
    IUserQueries userQueries,
    IOrganizationQueries organizations,
    IUnitOfWork unitOfWork,
    ICurrentUser currentUser,
    IPasswordHasher passwordHasher,
    TimeProvider clock,
    ILogger<CreateManagerHandler> logger)
    : IRequestHandler<CreateManagerCommand, Result<TemporaryCredentials>>
{
    public async ValueTask<Result<TemporaryCredentials>> Handle(CreateManagerCommand request, CancellationToken cancellationToken)
    {
        var organizationId = currentUser.OrganizationId!.Value;
        var phone = ContactNormalizer.NormalizePhone(request.Phone);
        var email = ContactNormalizer.NormalizeEmail(request.Email);

        if (phone is not null && await users.IsPhoneTakenAsync(phone, excludeId: null, cancellationToken))
            return IdentityErrors.PhoneTaken;
        if (email is not null && await users.IsEmailTakenAsync(email, excludeId: null, cancellationToken))
            return IdentityErrors.EmailTaken;

        await using var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken);

        // ID-BR-15: khóa hàng tổ chức ⇒ 2 request thêm song song không vượt giới hạn.
        await unitOfWork.LockForUpdateAsync<Organization>(organizationId, cancellationToken);
        var maxManagers = (await organizations.GetAsync(organizationId, cancellationToken))!.MaxManagers;
        var activeManagers = await userQueries.CountActiveManagersAsync(organizationId, cancellationToken);
        if (activeManagers >= maxManagers)
            return IdentityErrors.ManagerLimitReached;

        var now = clock.GetUtcNow();
        var temporaryPassword = TemporaryPasswordGenerator.Generate();
        var manager = User.CreateOrgManager(organizationId, request.FullName, phone, email, passwordHasher.Hash(temporaryPassword), now);

        users.Add(manager);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        logger.LogInformation("Manager {UserId} added to organization {OrganizationId} by {ActorId}",
            manager.Id, organizationId, currentUser.UserId);
        return new TemporaryCredentials(manager.Id, manager.Username, temporaryPassword, manager.TempPasswordExpiresAt!.Value);
    }
}
