using System.Linq.Expressions;
using FluentValidation;
using renting_room.Application.Common.Validation;
using renting_room.Domain.Billing;

namespace renting_room.Application.Billing;

// Sửa nháp: sửa tay, phụ thu / giảm tay, ghi chú
internal static class ManualLineRules
{
    public const decimal MaxAmount = 100_000_000;

    /// <summary>BL-BR-23 / BL-BR-27: phụ thu / giảm tay / hoàn trả — nội dung, lý do bắt buộc, số tiền dương ≤ 100 triệu.</summary>
    public static void Apply<T>(
        AbstractValidator<T> v, Expression<Func<T, InvoiceLineType>> type, Expression<Func<T, string>> description,
        Expression<Func<T, string>> note, Expression<Func<T, decimal>> amount, Expression<Func<T, decimal?>> quantity,
        Expression<Func<T, decimal?>> unitPrice)
    {
        v.RuleFor(type).Must(Invoice.IsManualType)
            .WithErrorCode("INVALID_LINE_TYPE").WithMessage("Chỉ thêm phụ thu (Surcharge), giảm trừ (ManualDiscount) hoặc hoàn trả (Refund).");
        v.RuleFor(description).RequiredText(200, "Nội dung");
        v.RuleFor(note).RequiredText(300, "Lý do");
        v.RuleFor(amount).Money(allowZero: false).LessThanOrEqualTo(MaxAmount).WithErrorCode("INVALID_AMOUNT");
        v.RuleFor(quantity).Must(q => q is null || (q > 0 && decimal.Round(q.Value, 2) == q)).WithErrorCode("OUT_OF_RANGE");
        v.RuleFor(unitPrice).Must(p => p is null || (p >= 0 && decimal.Round(p.Value, 2) == p)).WithErrorCode("INVALID_AMOUNT");
    }
}
