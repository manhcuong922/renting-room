using FluentValidation;
using renting_room.Domain.Identity;

namespace renting_room.Application.Common.Validation;

/// <summary>Quy tắc validation dùng chung cho tài khoản (ID-BR-01, ID-BR-05).</summary>
public static class IdentityRules
{
    public const int PasswordMinLength = 8;
    public const int PasswordMaxLength = 128;

    public static IRuleBuilderOptions<T, string> StrongPassword<T>(this IRuleBuilder<T, string> rule) =>
        rule
            .NotEmpty().WithErrorCode("REQUIRED").WithMessage("Mật khẩu là bắt buộc.")
            .MinimumLength(PasswordMinLength).WithErrorCode("WEAK_PASSWORD")
                .WithMessage($"Mật khẩu phải có ít nhất {PasswordMinLength} ký tự.")
            .MaximumLength(PasswordMaxLength).WithErrorCode("MAX_LENGTH")
                .WithMessage($"Mật khẩu tối đa {PasswordMaxLength} ký tự.")
            .Must(p => p.Any(char.IsLetter) && p.Any(char.IsDigit)).WithErrorCode("WEAK_PASSWORD")
                .WithMessage("Mật khẩu phải có cả chữ và số.");

    public static IRuleBuilderOptions<T, string?> VietnamPhone<T>(this IRuleBuilder<T, string?> rule) =>
        rule
            .Must(p => p is null || ContactNormalizer.NormalizePhone(p) is not null)
            .WithErrorCode("INVALID_PHONE")
            .WithMessage("Số điện thoại di động Việt Nam không hợp lệ.");

    public static IRuleBuilderOptions<T, string?> ValidEmail<T>(this IRuleBuilder<T, string?> rule) =>
        rule
            .Must(e => e is null || ContactNormalizer.NormalizeEmail(e) is not null)
            .WithErrorCode("INVALID_EMAIL")
            .WithMessage("Email không hợp lệ.");

    public static IRuleBuilderOptions<T, string> FullName<T>(this IRuleBuilder<T, string> rule) =>
        rule
            .Must(n => !string.IsNullOrWhiteSpace(n)).WithErrorCode("REQUIRED").WithMessage("Họ tên là bắt buộc.")
            .MaximumLength(200).WithErrorCode("MAX_LENGTH").WithMessage("Họ tên tối đa 200 ký tự.");
}
