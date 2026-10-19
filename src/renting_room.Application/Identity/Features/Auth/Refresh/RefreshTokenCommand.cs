using FluentValidation;
using Mediator;
using renting_room.Domain.Common;

namespace renting_room.Application.Identity.Auth.Refresh;

public sealed record RefreshTokenCommand(string RefreshToken, string? IpAddress) : IRequest<Result<AuthTokens>>;

public sealed class RefreshTokenCommandValidator : AbstractValidator<RefreshTokenCommand>
{
    public RefreshTokenCommandValidator()
    {
        RuleFor(x => x.RefreshToken)
            .NotEmpty().WithErrorCode("REQUIRED").WithMessage("Refresh token là bắt buộc.")
            .MaximumLength(200).WithErrorCode("MAX_LENGTH");
    }
}
