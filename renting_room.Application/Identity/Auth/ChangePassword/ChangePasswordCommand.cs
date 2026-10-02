using FluentValidation;
using Mediator;
using renting_room.Application.Common.Validation;
using renting_room.Domain.Common;

namespace renting_room.Application.Identity.Auth.ChangePassword;

public sealed record ChangePasswordCommand(string CurrentPassword, string NewPassword, string? IpAddress)
    : IRequest<Result<AuthTokens>>;

public sealed class ChangePasswordCommandValidator : AbstractValidator<ChangePasswordCommand>
{
    public ChangePasswordCommandValidator()
    {
        RuleFor(x => x.CurrentPassword)
            .NotEmpty().WithErrorCode("REQUIRED").WithMessage("Mật khẩu hiện tại là bắt buộc.")
            .MaximumLength(IdentityRules.PasswordMaxLength).WithErrorCode("MAX_LENGTH");

        RuleFor(x => x.NewPassword).StrongPassword();
    }
}
