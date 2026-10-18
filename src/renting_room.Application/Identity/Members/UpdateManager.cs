using FluentValidation;
using Mediator;
using Microsoft.EntityFrameworkCore;
using renting_room.Application.Common.Interfaces;
using renting_room.Application.Common.Validation;
using renting_room.Domain.Common;
using renting_room.Domain.Identity;

namespace renting_room.Application.Identity.Members;

// Sửa / khóa / mở / cấp lại mật khẩu / gỡ (chỉ chủ trọ, chỉ phó quản lý)
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
