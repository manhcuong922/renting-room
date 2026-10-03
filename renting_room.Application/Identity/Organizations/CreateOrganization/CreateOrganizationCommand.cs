using FluentValidation;
using Mediator;
using renting_room.Application.Common.Validation;
using renting_room.Domain.Common;

namespace renting_room.Application.Identity.Organizations.CreateOrganization;

public sealed record CreateOrganizationOwner(string FullName, string? Phone, string? Email);

public sealed record CreateOrganizationCommand(
    string Code,
    string Name,
    string? ContactName,
    string? ContactPhone,
    string? ContactEmail,
    string? TaxCode,
    string? Note,
    CreateOrganizationOwner Owner,
    string? Address = null) : IRequest<Result<CreateOrganizationResult>>;

/// <summary><see cref="TemporaryPassword"/> chỉ trả về đúng một lần — không lưu dạng rõ, không ghi log.</summary>
public sealed record CreateOrganizationResult(
    Guid OrganizationId,
    Guid OwnerUserId,
    string Username,
    string TemporaryPassword);

public sealed class CreateOrganizationCommandValidator : AbstractValidator<CreateOrganizationCommand>
{
    public CreateOrganizationCommandValidator()
    {
        RuleFor(x => x.Code)
            .NotEmpty().WithErrorCode("REQUIRED").WithMessage("Mã tổ chức là bắt buộc.")
            .Matches("^[A-Za-z0-9-]{3,32}$").WithErrorCode("INVALID_FORMAT")
                .WithMessage("Mã tổ chức gồm 3–32 ký tự chữ, số hoặc dấu gạch ngang.");

        RuleFor(x => x.Name)
            .Must(n => !string.IsNullOrWhiteSpace(n)).WithErrorCode("REQUIRED").WithMessage("Tên tổ chức là bắt buộc.")
            .MaximumLength(200).WithErrorCode("MAX_LENGTH");

        RuleFor(x => x.ContactName).MaximumLength(200).WithErrorCode("MAX_LENGTH");
        RuleFor(x => x.ContactPhone).VietnamPhone();
        RuleFor(x => x.ContactEmail).ValidEmail();
        RuleFor(x => x.TaxCode)
            .Matches(@"^\d{10}(-\d{3})?$").When(x => x.TaxCode is not null)
            .WithErrorCode("INVALID_TAX_CODE").WithMessage("Mã số thuế gồm 10 số hoặc dạng 10 số-3 số.");
        RuleFor(x => x.Note).MaximumLength(2000).WithErrorCode("MAX_LENGTH");

        RuleFor(x => x.Owner).NotNull().WithErrorCode("REQUIRED").WithMessage("Thông tin chủ trọ là bắt buộc.");
        When(x => x.Owner is not null, () =>
        {
            RuleFor(x => x.Owner.FullName).FullName();
            RuleFor(x => x.Owner.Phone).VietnamPhone();
            RuleFor(x => x.Owner.Email).ValidEmail();
            RuleFor(x => x.Owner)
                .Must(o => !string.IsNullOrWhiteSpace(o.Phone) || !string.IsNullOrWhiteSpace(o.Email))
                .WithErrorCode("USERNAME_REQUIRED")
                .WithMessage("Chủ trọ phải có số điện thoại hoặc email để đăng nhập.");
        });
    }
}
