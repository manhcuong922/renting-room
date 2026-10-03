using FluentValidation;
using renting_room.Application.Common.Models;
using renting_room.Domain.Common;

namespace renting_room.Application.Common.Validation;

public static class CommonRules
{
    public const decimal MaxMoney = 1_000_000_000m;

    public static IRuleBuilderOptions<T, string> RequiredText<T>(this IRuleBuilder<T, string> rule, int maxLength, string label) =>
        rule
            .Must(v => !string.IsNullOrWhiteSpace(v)).WithErrorCode("REQUIRED").WithMessage($"{label} là bắt buộc.")
            .MaximumLength(maxLength).WithErrorCode("MAX_LENGTH").WithMessage($"{label} tối đa {maxLength} ký tự.");

    /// <summary>
    /// Validate input lồng trong command nhưng client gửi ở cấp gốc của body: giữ nguyên tên field
    /// (không thêm tiền tố tên thuộc tính) để key trong <c>errors</c> khớp đường dẫn JSON client gửi.
    /// </summary>
    public static IRuleBuilderOptionsConditions<T, TInput> FlattenedValidator<T, TInput>(
        this IRuleBuilder<T, TInput> rule, IValidator<TInput> validator) =>
        rule.Custom((input, context) =>
        {
            if (input is null)
                return;

            foreach (var failure in validator.Validate(input).Errors)
                context.AddFailure(failure);
        });

    public static IRuleBuilderOptions<T, string?> OptionalText<T>(this IRuleBuilder<T, string?> rule, int maxLength) =>
        rule.MaximumLength(maxLength).WithErrorCode("MAX_LENGTH").WithMessage($"Tối đa {maxLength} ký tự.");

    public static IRuleBuilderOptions<T, string> Code<T>(this IRuleBuilder<T, string> rule, int maxLength, string label) =>
        rule
            .NotEmpty().WithErrorCode("REQUIRED").WithMessage($"{label} là bắt buộc.")
            .Matches($"^[A-Za-z0-9._/-]{{1,{maxLength}}}$").WithErrorCode("INVALID_FORMAT")
            .WithMessage($"{label} gồm tối đa {maxLength} ký tự chữ, số, '.', '_', '/' hoặc '-'.");

    /// <summary>Tiền VND: số nguyên, 0 ≤ x ≤ 1 tỷ.</summary>
    public static IRuleBuilderOptions<T, decimal> Money<T>(this IRuleBuilder<T, decimal> rule, bool allowZero = true) =>
        rule
            .Must(v => (allowZero ? v >= 0 : v > 0) && v <= MaxMoney && decimal.Truncate(v) == v)
            .WithErrorCode("INVALID_AMOUNT")
            .WithMessage(allowZero ? "Số tiền phải là số nguyên từ 0 đến 1 tỷ." : "Số tiền phải là số nguyên lớn hơn 0 và không quá 1 tỷ.");

    public static IRuleBuilderOptions<T, decimal?> OptionalMoney<T>(this IRuleBuilder<T, decimal?> rule) =>
        rule
            .Must(v => v is null || (v >= 0 && v <= MaxMoney && decimal.Truncate(v.Value) == v))
            .WithErrorCode("INVALID_AMOUNT").WithMessage("Số tiền phải là số nguyên từ 0 đến 1 tỷ.");

    public static IRuleBuilderOptions<T, string> IdNumber<T>(this IRuleBuilder<T, string> rule, Func<T, IdDocumentType> type) =>
        rule
            .NotEmpty().WithErrorCode("REQUIRED").WithMessage("Số giấy tờ là bắt buộc.")
            .Must((model, value) => IdDocumentNumber.IsValid(type(model), IdDocumentNumber.Normalize(value)))
            .WithErrorCode("INVALID_ID_NUMBER")
            .WithMessage("Số giấy tờ không hợp lệ (CCCD: 12 chữ số; CMND: 9 chữ số; hộ chiếu: 6–20 ký tự chữ/số).");

    public static IRuleBuilderOptions<T, int> Page<T>(this IRuleBuilder<T, int> rule) =>
        rule.GreaterThanOrEqualTo(1).WithErrorCode("OUT_OF_RANGE");

    public static IRuleBuilderOptions<T, int> PageSize<T>(this IRuleBuilder<T, int> rule) =>
        rule.InclusiveBetween(1, Paging.MaxPageSize).WithErrorCode("OUT_OF_RANGE");
}
