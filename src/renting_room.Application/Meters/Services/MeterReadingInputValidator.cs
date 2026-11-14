using System.Linq.Expressions;
using FluentValidation;

namespace renting_room.Application.Meters;

/// <summary>
/// M-19: chỉ số gửi kèm kích hoạt / chuyển phòng / hóa đơn trả phòng / thanh lý. Giá trị âm, quá lớn, quá 2 số lẻ hoặc trùng công tơ
/// làm <c>Meter.GuardValue</c> / <c>ToDictionary</c> ném lỗi ⇒ 500; kiểm ở biên để trả 400 theo từng trường.
/// </summary>
public sealed class MeterReadingInputValidator : AbstractValidator<MeterReadingInput>
{
    public MeterReadingInputValidator()
    {
        RuleFor(x => x.MeterId).NotEmpty().WithErrorCode("REQUIRED");
        // null = "dùng số mới nhất" (chỉ số nhận phòng) — hợp lệ.
        RuleFor(x => x.Value)
            .Must(v => v is null || MeterMapping.IsValidValue(v.Value))
            .WithErrorCode("INVALID_READING").WithMessage("Chỉ số 0–99.999.999,99, tối đa 2 số lẻ.");
    }
}

public static class MeterReadingRules
{
    /// <summary>Kiểm từng chỉ số + mỗi công tơ chỉ một chỉ số trong cùng lệnh.</summary>
    public static void RuleForMeterReadings<T>(this AbstractValidator<T> validator, Expression<Func<T, IEnumerable<MeterReadingInput>?>> readings)
    {
        validator.RuleForEach(readings).SetValidator(new MeterReadingInputValidator());
        validator.RuleFor(readings)
            .Must(list => list is null || list.Select(i => i.MeterId).Distinct().Count() == list.Count())
            .WithErrorCode("DUPLICATE_METER").WithMessage("Mỗi công tơ chỉ gửi một chỉ số.");
    }
}
