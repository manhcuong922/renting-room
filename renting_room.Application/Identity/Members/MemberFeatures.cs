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

// ============================================================ Queries (chủ trọ + phó quản lý)

public sealed record ListMembersQuery(bool IncludeRemoved = false) : IRequest<IReadOnlyList<MemberDto>>;

public sealed class ListMembersHandler(IAppDbContext db, ICurrentUser currentUser)
    : IRequestHandler<ListMembersQuery, IReadOnlyList<MemberDto>>
{
    public async ValueTask<IReadOnlyList<MemberDto>> Handle(ListMembersQuery request, CancellationToken cancellationToken)
    {
        var organizationId = currentUser.OrganizationId;
        return await db.Users.AsNoTracking()
            .Where(u => u.OrganizationId == organizationId && (request.IncludeRemoved || u.Status != UserStatus.Removed))
            .OrderBy(u => u.Role).ThenBy(u => u.FullName)
            .ToDto()
            .ToListAsync(cancellationToken);
    }
}

public sealed record GetMemberQuery(Guid Id) : IRequest<Result<MemberDto>>;

public sealed class GetMemberHandler(IAppDbContext db, ICurrentUser currentUser) : IRequestHandler<GetMemberQuery, Result<MemberDto>>
{
    public async ValueTask<Result<MemberDto>> Handle(GetMemberQuery request, CancellationToken cancellationToken)
    {
        var organizationId = currentUser.OrganizationId;
        var member = await db.Users.AsNoTracking()
            .Where(u => u.Id == request.Id && u.OrganizationId == organizationId)
            .ToDto()
            .FirstOrDefaultAsync(cancellationToken);

        return member is null ? IdentityErrors.MemberNotFound : member;
    }
}

// ============================================================ Thêm phó quản lý (chỉ chủ trọ)

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

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

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

// ============================================================ Sửa / khóa / mở / cấp lại mật khẩu / gỡ (chỉ chủ trọ, chỉ phó quản lý)

public sealed record UpdateManagerCommand(Guid Id, string FullName, string? Phone, string? Email, uint Version) : IRequest<Result<MemberDto>>;

public sealed class UpdateManagerCommandValidator : AbstractValidator<UpdateManagerCommand>
{
    public UpdateManagerCommandValidator()
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

public sealed class UpdateManagerHandler(
    IAppDbContext db, ICurrentUser currentUser, IUserSessionStore sessionStore)
    : IRequestHandler<UpdateManagerCommand, Result<MemberDto>>
{
    public async ValueTask<Result<MemberDto>> Handle(UpdateManagerCommand request, CancellationToken cancellationToken)
    {
        var manager = await ManagerLookup.FindAsync(db, currentUser, request.Id, cancellationToken);
        if (manager.IsFailure)
            return manager.Error!;

        var user = manager.Value!;
        var phone = ContactNormalizer.NormalizePhone(request.Phone);
        var email = ContactNormalizer.NormalizeEmail(request.Email);
        if (phone is not null && await db.Users.AnyAsync(u => u.PhoneNormalized == phone && u.Id != user.Id, cancellationToken))
            return IdentityErrors.PhoneTaken;
        if (email is not null && await db.Users.AnyAsync(u => u.EmailNormalized == email && u.Id != user.Id, cancellationToken))
            return IdentityErrors.EmailTaken;

        db.SetExpectedVersion(user, request.Version);
        var updated = user.UpdateProfile(request.FullName, phone, email);
        if (updated.IsFailure)
            return updated.Error!;

        await db.SaveChangesAsync(cancellationToken);
        sessionStore.Invalidate(user.Id);

        return await db.Users.AsNoTracking().Where(u => u.Id == user.Id).ToDto().FirstAsync(cancellationToken);
    }
}

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

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        if (request.Action != ManagerAction.Unlock)
            await AccountSessions.RevokeAllAsync(db, user.Id, now, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        sessionStore.Invalidate(user.Id);

        logger.LogInformation("Manager {UserId} {Action} by {ActorId}", user.Id, request.Action, currentUser.UserId);
        return Result.Success();
    }
}

public sealed record ResetManagerPasswordCommand(Guid Id) : IRequest<Result<TemporaryCredentials>>;

public sealed class ResetManagerPasswordHandler(
    IAppDbContext db, ICurrentUser currentUser, IPasswordHasher passwordHasher, IUserSessionStore sessionStore, TimeProvider clock)
    : IRequestHandler<ResetManagerPasswordCommand, Result<TemporaryCredentials>>
{
    public async ValueTask<Result<TemporaryCredentials>> Handle(ResetManagerPasswordCommand request, CancellationToken cancellationToken)
    {
        var manager = await ManagerLookup.FindAsync(db, currentUser, request.Id, cancellationToken);
        if (manager.IsFailure)
            return manager.Error!;

        return await PasswordReset.ResetAsync(db, passwordHasher, sessionStore, clock, manager.Value!, cancellationToken);
    }
}

internal static class ManagerLookup
{
    /// <summary>Phó quản lý cùng tổ chức với người gọi. Khác tổ chức ⇒ 404 (không lộ tồn tại); là chủ trọ ⇒ 422.</summary>
    public static async Task<Result<User>> FindAsync(IAppDbContext db, ICurrentUser currentUser, Guid id, CancellationToken ct)
    {
        var organizationId = currentUser.OrganizationId;
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == id && u.OrganizationId == organizationId, ct);
        if (user is null)
            return IdentityErrors.MemberNotFound;
        if (user.Role != UserRole.OrgManager)
            return IdentityErrors.CannotModifyOwner;

        return user;
    }
}

internal static class PasswordReset
{
    public static async Task<Result<TemporaryCredentials>> ResetAsync(
        IAppDbContext db, IPasswordHasher passwordHasher, IUserSessionStore sessionStore, TimeProvider clock, User user, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var temporaryPassword = TemporaryPasswordGenerator.Generate();
        var reset = user.ResetPassword(passwordHasher.Hash(temporaryPassword), now);
        if (reset.IsFailure)
            return reset.Error!;

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await db.SaveChangesAsync(ct);
        await AccountSessions.RevokeAllAsync(db, user.Id, now, ct);
        await transaction.CommitAsync(ct);
        sessionStore.Invalidate(user.Id);

        return new TemporaryCredentials(user.Id, user.Username, temporaryPassword, user.TempPasswordExpiresAt!.Value);
    }
}
