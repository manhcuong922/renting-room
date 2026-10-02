using FluentValidation;
using Mediator;
using renting_room.Domain.Common;

namespace renting_room.Application.Identity.Auth.Login;

/// <param name="Username">Số điện thoại hoặc email.</param>
/// <param name="IpAddress">Do API gán từ kết nối, không lấy từ body.</param>
public sealed record LoginCommand(string Username, string Password, string? IpAddress) : IRequest<Result<AuthTokens>>;

public sealed class LoginCommandValidator : AbstractValidator<LoginCommand>
{
    public LoginCommandValidator()
    {
        RuleFor(x => x.Username)
            .NotEmpty().WithErrorCode("REQUIRED").WithMessage("Tên đăng nhập là bắt buộc.")
            .MaximumLength(254).WithErrorCode("MAX_LENGTH");

        RuleFor(x => x.Password)
            .NotEmpty().WithErrorCode("REQUIRED").WithMessage("Mật khẩu là bắt buộc.")
            .MaximumLength(128).WithErrorCode("MAX_LENGTH");
    }
}
