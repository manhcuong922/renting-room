using FluentValidation;
using Mediator;
using Microsoft.EntityFrameworkCore;
using renting_room.Application.Common.Interfaces;
using renting_room.Application.Common.Validation;
using renting_room.Domain.Common;
using renting_room.Domain.Identity;

namespace renting_room.Application.Identity.Me;

/// <summary>ID-UC-12: người dùng tự sửa họ tên, SĐT, email của mình.</summary>
public sealed record UpdateMeCommand(string FullName, string? Phone, string? Email) : IRequest<Result>;

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

/// <summary>SĐT / email là tên đăng nhập, unique toàn hệ thống (ID-BR-02); đổi ⇒ mọi phiên bị vô hiệu, phải đăng nhập lại.</summary>
public sealed class UpdateMeHandler(IAppDbContext db, ICurrentUser currentUser, IUserSessionStore sessionStore) : IRequestHandler<UpdateMeCommand, Result>
{
    public async ValueTask<Result> Handle(UpdateMeCommand request, CancellationToken cancellationToken)
    {
        var userId = currentUser.RequireUserId();
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);
        if (user is null)
            return Result.Failure(IdentityErrors.UserNotFound);

        var phone = ContactNormalizer.NormalizePhone(request.Phone);
        var email = ContactNormalizer.NormalizeEmail(request.Email);
        if (phone is not null && await db.Users.AnyAsync(u => u.PhoneNormalized == phone && u.Id != user.Id, cancellationToken))
            return Result.Failure(IdentityErrors.PhoneTaken);
        if (email is not null && await db.Users.AnyAsync(u => u.EmailNormalized == email && u.Id != user.Id, cancellationToken))
            return Result.Failure(IdentityErrors.EmailTaken);

        var updated = user.UpdateProfile(request.FullName, phone, email);
        if (updated.IsFailure)
            return updated;

        await db.SaveChangesAsync(cancellationToken);
        sessionStore.Invalidate(user.Id);
        return updated;
    }
}
