using FluentValidation;
using renting_room.Application.Common.Validation;
using renting_room.Domain.Common;
using renting_room.Domain.Identity;
using renting_room.Domain.Renters;

namespace renting_room.Application.Renters;

public sealed record RenterDto(
    Guid Id,
    string FullName,
    DateOnly DateOfBirth,
    Gender Gender,
    string? Phone,
    string? Email,
    string Nationality,
    IdDocumentType? IdType,
    string? IdNumberMasked,
    DateOnly? IdIssueDate,
    string? IdIssuePlace,
    string? PermanentAddress,
    string? Occupation,
    string? Workplace,
    string? EmergencyContactName,
    string? EmergencyContactPhone,
    string? Note,
    DateTimeOffset CreatedAt,
    string Version,
    DateTimeOffset? AnonymizedAt = null,
    DateTimeOffset? ArchivedAt = null);

internal static class RenterProjection
{
    public static string? NormalizePhone(string? phone)
    {
        if (string.IsNullOrWhiteSpace(phone))
            return null;

        var vietnam = ContactNormalizer.NormalizePhone(phone);
        if (vietnam is not null)
            return vietnam;

        var digits = new string(phone.Where(char.IsDigit).ToArray());
        return phone.TrimStart().StartsWith('+') && digits.Length is >= 8 and <= 15 ? $"+{digits}" : null;
    }
}

/// <param name="IdNumber">Khi sửa: null = giữ nguyên số giấy tờ đang lưu.</param>
public sealed record RenterInput(
    string FullName,
    DateOnly DateOfBirth,
    Gender Gender,
    string? Phone,
    string? Email,
    string? Nationality,
    IdDocumentType? IdType,
    string? IdNumber,
    DateOnly? IdIssueDate,
    string? IdIssuePlace,
    string? PermanentAddress,
    string? Occupation,
    string? Workplace,
    string? EmergencyContactName,
    string? EmergencyContactPhone,
    string? Note)
{
    public RenterProfile ToProfile() => new(
        FullName, DateOfBirth, Gender, RenterProjection.NormalizePhone(Phone), Email, Nationality ?? "VN", IdIssueDate, IdIssuePlace,
        PermanentAddress, Occupation, Workplace, EmergencyContactName, RenterProjection.NormalizePhone(EmergencyContactPhone) ?? EmergencyContactPhone,
        Note);
}

internal sealed class RenterInputValidator : AbstractValidator<RenterInput>
{
    public RenterInputValidator(TimeProvider clock, bool idNumberRequired)
    {
        RuleFor(x => x.FullName).RequiredText(200, "Họ tên");
        RuleFor(x => x.DateOfBirth)
            .Must(d => d >= new DateOnly(1900, 1, 1) && d <= clock.GetUtcNow().ToBusinessDate())
            .WithErrorCode("INVALID_DATE_OF_BIRTH").WithMessage("Ngày sinh không hợp lệ.");
        RuleFor(x => x.Gender).IsInEnum();
        RuleFor(x => x.IdType).IsInEnum().When(x => x.IdType is not null);
        RuleFor(x => x.IdType).NotNull().When(x => x.IdNumber is not null).WithErrorCode("REQUIRED").WithMessage("Chọn loại giấy tờ.");
        RuleFor(x => x.Phone)
            .Must(p => p is null || RenterProjection.NormalizePhone(p) is not null)
            .WithErrorCode("INVALID_PHONE").WithMessage("Số điện thoại không hợp lệ (di động VN hoặc dạng +mã nước).");
        RuleFor(x => x.Email).ValidEmail();
        RuleFor(x => x.Nationality)
            .Matches("^[A-Za-z]{2}$").When(x => x.Nationality is not null)
            .WithErrorCode("INVALID_NATIONALITY").WithMessage("Quốc tịch theo mã ISO 2 chữ cái (VD: VN, KR).");
        When(x => x.IdNumber is not null && x.IdType is not null, () => RuleFor(x => x.IdNumber!).IdNumber(x => x.IdType!.Value));
        // RT-BR-01: từ 14 tuổi bắt buộc giấy tờ (tạo mới; khi sửa kiểm ở handler vì null = giữ số cũ).
        if (idNumberRequired)
            RuleFor(x => x.IdNumber)
                .NotEmpty().When(x => x.DateOfBirth.AgeOn(clock.GetUtcNow().ToBusinessDate()) >= Renter.IdRequiredAge)
                .WithErrorCode("REQUIRED").WithMessage($"Từ {Renter.IdRequiredAge} tuổi phải có số giấy tờ.");
        RuleFor(x => x.IdIssueDate)
            .Must((x, d) => d is null || (d >= x.DateOfBirth && d <= clock.GetUtcNow().ToBusinessDate()))
            .WithErrorCode("INVALID_DATE").WithMessage("Ngày cấp phải sau ngày sinh và không ở tương lai.");
        RuleFor(x => x.IdIssuePlace).OptionalText(200);
        RuleFor(x => x.PermanentAddress).OptionalText(500);
        RuleFor(x => x.Occupation).OptionalText(200);
        RuleFor(x => x.Workplace).OptionalText(200);
        RuleFor(x => x.EmergencyContactName).OptionalText(200);
        RuleFor(x => x.EmergencyContactPhone).OptionalText(20);
        RuleFor(x => x.Note).OptionalText(2000);
    }
}
