using FluentValidation;
using Mediator;
using renting_room.Application.Common.Interfaces;
using renting_room.Application.Common.Validation;
using renting_room.Domain.Common;
using renting_room.Domain.Identity;

namespace renting_room.Application.Identity.Me;

public sealed record UpdateMeCommand(string FullName, string? Phone, string? Email, string? CurrentPassword = null) : IRequest<Result>;

public sealed class UpdateMeCommandValidator : AbstractValidator<UpdateMeCommand>
{
    public UpdateMeCommandValidator()
    {
        RuleFor(x => x.FullName).FullName();
        RuleFor(x => x.Phone).VietnamPhone();
        RuleFor(x => x.Email).ValidEmail();
        RuleFor(x => x)
            .Must(x => !string.IsNullOrWhiteSpace(x.Phone) || !string.IsNullOrWhiteSpace(x.Email))
            .OverridePropertyName("phone").WithErrorCode("USERNAME_REQUIRED")
            .WithMessage("Phải có số điện thoại hoặc email để đăng nhập.");
    }
}

/// <summary>
/// SĐT / email là tên đăng nhập, unique toàn hệ thống (ID-BR-02); đổi ⇒ phải nhập mật khẩu hiện tại (phiên bỏ quên không chiếm được tài khoản),
/// mọi phiên bị vô hiệu, phải đăng nhập lại.
/// </summary>
public sealed class UpdateMeHandler(
    IUserRepository users, IUnitOfWork unitOfWork, ICurrentUser currentUser, IUserSessionStore sessionStore, IPasswordHasher passwordHasher,
    TimeProvider clock)
    : IRequestHandler<UpdateMeCommand, Result>
{
    public async ValueTask<Result> Handle(UpdateMeCommand request, CancellationToken cancellationToken)
    {
        if (currentUser.Role == UserRole.OrgManager)
            return Result.Failure(IdentityErrors.ProfileManagedByOwner);

        var userId = currentUser.RequireUserId();
        var user = await users.GetForUpdateAsync(userId, cancellationToken);
        if (user is null)
            return Result.Failure(IdentityErrors.UserNotFound);

        var phone = ContactNormalizer.NormalizePhone(request.Phone);
        var email = ContactNormalizer.NormalizeEmail(request.Email);
        if (phone != user.PhoneNormalized || email != user.EmailNormalized)
        {
            var confirmed = await ConfirmPasswordAsync(user, request.CurrentPassword, cancellationToken);
            if (confirmed.IsFailure)
                return confirmed;
        }
        if (phone is not null && await users.IsPhoneTakenAsync(phone, user.Id, cancellationToken))
            return Result.Failure(IdentityErrors.PhoneTaken);
        if (email is not null && await users.IsEmailTakenAsync(email, user.Id, cancellationToken))
            return Result.Failure(IdentityErrors.EmailTaken);

        var updated = user.UpdateProfile(request.FullName, phone, email);
        if (updated.IsFailure)
            return updated;

        await unitOfWork.SaveChangesAsync(cancellationToken);
        sessionStore.Invalidate(user.Id);
        return updated;
    }

    /// <summary>Sai mật khẩu tính vào bộ đếm khóa tài khoản như đăng nhập sai — access token bị lộ không dò được mật khẩu.</summary>
    private async Task<Result> ConfirmPasswordAsync(User user, string? currentPassword, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(currentPassword))
            return Result.Failure(IdentityErrors.CurrentPasswordRequired);
        var now = clock.GetUtcNow();
        if (user.IsLockedOut(now))
            return Result.Failure(IdentityErrors.AccountLocked);
        if (passwordHasher.Verify(user.PasswordHash, currentPassword) != PasswordCheckResult.Failed)
            return Result.Success();

        await users.RegisterFailedLoginAsync(user.Id, now, ct);
        return Result.Failure(IdentityErrors.InvalidCurrentPassword);
    }
}
