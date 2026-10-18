using FluentValidation;
using renting_room.Application.Common.Interfaces;
using renting_room.Application.Common.Validation;
using renting_room.Domain.Common;
using renting_room.Domain.Identity;
using renting_room.Domain.Properties;

namespace renting_room.Application.Properties;

// Bên cho thuê (PR-BR-12 → 17)
/// <summary>Các trường khai bên cho thuê — dùng chung cho bên cho thuê mặc định của tổ chức (chủ trọ) và bên cho thuê riêng của khu.</summary>
public interface ILessorFields
{
    LessorType Type { get; }
    string Name { get; }
    string Address { get; }
    string Phone { get; }
    string? Email { get; }
    IdDocumentType? IdType { get; }
    string? IdNumber { get; }
    DateOnly? IdIssueDate { get; }
    string? IdIssuePlace { get; }
    DateOnly? DateOfBirth { get; }
    string? TaxCode { get; }
    string? RepresentativeName { get; }
    string? RepresentativeTitle { get; }
    string? AuthorizationDocNo { get; }
    DateOnly? AuthorizationDocDate { get; }
}

internal static class LessorRules
{
    public static void Lessor<T>(this AbstractValidator<T> v, TimeProvider clock) where T : ILessorFields
    {
        v.RuleFor(x => x.Type).IsInEnum();
        v.RuleFor(x => x.Name).RequiredText(200, "Tên bên cho thuê");
        v.RuleFor(x => x.Address).RequiredText(500, "Địa chỉ bên cho thuê");
        v.RuleFor(x => x.Phone)
            .NotEmpty().WithErrorCode("REQUIRED")
            .Must(p => ContactNormalizer.NormalizePhone(p) is not null).WithErrorCode("INVALID_PHONE")
            .WithMessage("Số điện thoại di động Việt Nam không hợp lệ.");
        v.RuleFor(x => x.Email).ValidEmail();
        v.RuleFor(x => x.IdIssuePlace).OptionalText(200);
        v.RuleFor(x => x.AuthorizationDocNo).OptionalText(50);

        v.When(x => x.Type == LessorType.Individual, () =>
        {
            v.RuleFor(x => x.IdType).NotNull().WithErrorCode("REQUIRED").WithMessage("Loại giấy tờ là bắt buộc với cá nhân.");
            v.RuleFor(x => x.DateOfBirth)
                .NotNull().WithErrorCode("REQUIRED")
                .Must(d => d is null || d.Value.AgeOn(clock.GetUtcNow().ToBusinessDate()) >= LessorDetails.MinimumAge)
                .WithErrorCode("LESSOR_UNDERAGE").WithMessage("Bên cho thuê là cá nhân phải đủ 18 tuổi.");
        });
        v.When(x => x.IdNumber is not null, () =>
            v.RuleFor(x => x.IdNumber!).IdNumber(x => x.IdType ?? IdDocumentType.CitizenId));
        v.When(x => x.Type == LessorType.Organization, () =>
        {
            v.RuleFor(x => x.TaxCode)
                .NotEmpty().WithErrorCode("REQUIRED")
                .Matches(@"^\d{10}(-\d{3})?$").WithErrorCode("INVALID_TAX_CODE");
            v.RuleFor(x => x.RepresentativeName!).RequiredText(200, "Người đại diện");
            v.RuleFor(x => x.RepresentativeTitle!).RequiredText(100, "Chức vụ người đại diện");
        });
        // PR-BR-13: có số giấy ủy quyền thì phải có ngày (và ngược lại).
        v.RuleFor(x => x.AuthorizationDocDate)
            .NotNull().When(x => !string.IsNullOrWhiteSpace(x.AuthorizationDocNo)).WithErrorCode("REQUIRED");
    }

    /// <summary>Dựng bên cho thuê từ dữ liệu nhập; số giấy tờ bỏ trống ⇒ giữ số đang lưu (<paramref name="existing"/>).</summary>
    public static Result<LessorDetails> Build(
        ILessorFields request, LessorDetails? existing, IPersonalDataProtector protector, Guid organizationId)
    {
        // Đổi loại giấy tờ mà không nhập số mới ⇒ số cũ không còn đúng loại (VD CCCD 12 số gắn nhãn hộ chiếu).
        if (request.IdNumber is null && request.IdType is not null && request.IdType != existing?.IdType)
            return LessorErrors.IdNumberRequired;

        var encrypted = existing?.IdNumberEncrypted;
        var last4 = existing?.IdNumberLast4;
        if (request.IdNumber is not null && request.IdType is { } idType)
        {
            var protectedNumber = protector.ProtectIdNumber(organizationId, idType, request.IdNumber);
            encrypted = protectedNumber.Encrypted;
            last4 = protectedNumber.Last4;
        }

        if (request.IdType is null)
        {
            encrypted = null;
            last4 = null;
        }

        return new LessorDetails(
            request.Type,
            request.Name,
            request.Address,
            ContactNormalizer.NormalizePhone(request.Phone)!,
            ContactNormalizer.NormalizeEmail(request.Email),
            request.IdType,
            encrypted,
            last4,
            request.IdIssueDate,
            request.IdIssuePlace,
            request.Type == LessorType.Individual ? request.DateOfBirth : null,
            request.Type == LessorType.Organization ? request.TaxCode : null,
            request.RepresentativeName,
            request.RepresentativeTitle,
            request.AuthorizationDocNo,
            request.AuthorizationDocDate);
    }
}

// Ngân hàng & nội quy
internal static class LessorErrors
{
    public static readonly Error IdNumberRequired =
        Error.Validation("ID_NUMBER_REQUIRED", "Đổi loại giấy tờ thì phải nhập lại số giấy tờ.");
}
