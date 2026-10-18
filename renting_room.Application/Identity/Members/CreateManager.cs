using FluentValidation;
using Mediator;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using renting_room.Application.Common.Interfaces;
using renting_room.Application.Common.Validation;
using renting_room.Application.Identity.Organizations;
using renting_room.Domain.Common;
using renting_room.Domain.Identity;

namespace renting_room.Application.Identity.Members;

// Thêm phó quản lý (chỉ chủ trọ)
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
    IAppDbContext db,
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

        if (phone is not null && await db.Users.AnyAsync(u => u.PhoneNormalized == phone, cancellationToken))
            return IdentityErrors.PhoneTaken;
        if (email is not null && await db.Users.AnyAsync(u => u.EmailNormalized == email, cancellationToken))
            return IdentityErrors.EmailTaken;

        await using var transaction = await db.BeginTransactionAsync(cancellationToken);

        // ID-BR-15: khóa hàng tổ chức ⇒ 2 request thêm song song không vượt giới hạn.
        await db.LockForUpdateAsync<Organization>(organizationId, cancellationToken);
        var maxManagers = await db.Organizations.Where(o => o.Id == organizationId).Select(o => o.MaxManagers).FirstAsync(cancellationToken);
        var activeManagers = await db.Users.CountAsync(
            u => u.OrganizationId == organizationId && u.Role == UserRole.OrgManager && u.Status != UserStatus.Removed, cancellationToken);
        if (activeManagers >= maxManagers)
            return IdentityErrors.ManagerLimitReached;

        var now = clock.GetUtcNow();
        var temporaryPassword = TemporaryPasswordGenerator.Generate();
        var manager = User.CreateOrgManager(organizationId, request.FullName, phone, email, passwordHasher.Hash(temporaryPassword), now);

        db.Users.Add(manager);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        logger.LogInformation("Manager {UserId} added to organization {OrganizationId} by {ActorId}",
            manager.Id, organizationId, currentUser.UserId);
        return new TemporaryCredentials(manager.Id, manager.Username, temporaryPassword, manager.TempPasswordExpiresAt!.Value);
    }
}
