using FluentValidation;
using Mediator;
using Microsoft.EntityFrameworkCore;
using renting_room.Application.Common.Interfaces;
using renting_room.Application.Common.Security;
using renting_room.Application.Common.Validation;
using renting_room.Domain.Common;
using renting_room.Domain.Identity;
using renting_room.Domain.Properties;

namespace renting_room.Application.Properties;

// ============================================================ Bên cho thuê (PR-BR-12 → 15)

/// <param name="IdNumber">Null = giữ nguyên số giấy tờ đang lưu (client chỉ thấy 4 số cuối nên không gửi lại được).</param>
public sealed record UpdateLessorCommand(
    Guid PropertyId,
    LessorType Type,
    string Name,
    string Address,
    string Phone,
    string? Email,
    IdDocumentType? IdType,
    string? IdNumber,
    DateOnly? IdIssueDate,
    string? IdIssuePlace,
    DateOnly? DateOfBirth,
    string? TaxCode,
    string? RepresentativeName,
    string? RepresentativeTitle,
    string? AuthorizationDocNo,
    DateOnly? AuthorizationDocDate) : IRequest<Result<PropertyDetailDto>>;

public sealed class UpdateLessorCommandValidator : AbstractValidator<UpdateLessorCommand>
{
    public UpdateLessorCommandValidator(TimeProvider clock)
    {
        RuleFor(x => x.Type).IsInEnum();
        RuleFor(x => x.Name).RequiredText(200, "Tên bên cho thuê");
        RuleFor(x => x.Address).RequiredText(500, "Địa chỉ bên cho thuê");
        RuleFor(x => x.Phone)
            .NotEmpty().WithErrorCode("REQUIRED")
            .Must(p => ContactNormalizer.NormalizePhone(p) is not null).WithErrorCode("INVALID_PHONE")
            .WithMessage("Số điện thoại di động Việt Nam không hợp lệ.");
        RuleFor(x => x.Email).ValidEmail();
        RuleFor(x => x.IdIssuePlace).OptionalText(200);
        RuleFor(x => x.AuthorizationDocNo).OptionalText(50);

        When(x => x.Type == LessorType.Individual, () =>
        {
            RuleFor(x => x.IdType).NotNull().WithErrorCode("REQUIRED").WithMessage("Loại giấy tờ là bắt buộc với cá nhân.");
            RuleFor(x => x.DateOfBirth)
                .NotNull().WithErrorCode("REQUIRED")
                .Must(d => d is null || d.Value.AgeOn(clock.GetUtcNow().ToBusinessDate()) >= LessorDetails.MinimumAge)
                .WithErrorCode("LESSOR_UNDERAGE").WithMessage("Bên cho thuê là cá nhân phải đủ 18 tuổi.");
        });
        When(x => x.IdNumber is not null, () =>
            RuleFor(x => x.IdNumber!).IdNumber(x => x.IdType ?? IdDocumentType.CitizenId));
        When(x => x.Type == LessorType.Organization, () =>
        {
            RuleFor(x => x.TaxCode)
                .NotEmpty().WithErrorCode("REQUIRED")
                .Matches(@"^\d{10}(-\d{3})?$").WithErrorCode("INVALID_TAX_CODE");
            RuleFor(x => x.RepresentativeName!).RequiredText(200, "Người đại diện");
            RuleFor(x => x.RepresentativeTitle!).RequiredText(100, "Chức vụ người đại diện");
        });
        // PR-BR-13: có số giấy ủy quyền thì phải có ngày (và ngược lại).
        RuleFor(x => x.AuthorizationDocDate)
            .NotNull().When(x => !string.IsNullOrWhiteSpace(x.AuthorizationDocNo)).WithErrorCode("REQUIRED");
    }
}

public sealed class UpdateLessorHandler(IAppDbContext db, ICurrentUser currentUser, IPersonalDataProtector protector, TimeProvider clock)
    : IRequestHandler<UpdateLessorCommand, Result<PropertyDetailDto>>
{
    public async ValueTask<Result<PropertyDetailDto>> Handle(UpdateLessorCommand request, CancellationToken cancellationToken)
    {
        var property = await db.Properties.FirstOrDefaultAsync(p => p.Id == request.PropertyId, cancellationToken);
        if (property is null)
            return PropertyErrors.PropertyNotFound;

        // Đổi loại giấy tờ mà không nhập số mới ⇒ số cũ không còn đúng loại (VD CCCD 12 số gắn nhãn hộ chiếu).
        if (request.IdNumber is null && request.IdType is not null && request.IdType != property.LessorIdType)
            return LessorErrors.IdNumberRequired;

        byte[]? encrypted = property.LessorIdNumberEncrypted;
        var last4 = property.LessorIdNumberLast4;
        if (request.IdNumber is not null && request.IdType is { } idType)
        {
            var protectedNumber = protector.ProtectIdNumber(currentUser.OrganizationId!.Value, idType, request.IdNumber);
            encrypted = protectedNumber.Encrypted;
            last4 = protectedNumber.Last4;
        }

        if (request.IdType is null)
        {
            encrypted = null;
            last4 = null;
        }

        property.UpdateLessor(new LessorDetails(
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
            request.AuthorizationDocDate));

        await db.SaveChangesAsync(cancellationToken);
        return property.ToDetail(clock.GetUtcNow().ToBusinessDate());
    }
}

/// <summary>Xem số giấy tờ đầy đủ của bên cho thuê — chỉ người có quyền (ID-BR-22), luôn ghi log ai xem (C-10, ID-BR-21).</summary>
public sealed record RevealLessorIdNumberQuery(Guid PropertyId) : IRequest<Result<string>>;

public sealed class RevealLessorIdNumberHandler(
    IAppDbContext db, ICurrentUser currentUser, IPersonalDataProtector protector, IAuditTrail auditTrail)
    : IRequestHandler<RevealLessorIdNumberQuery, Result<string>>
{
    public async ValueTask<Result<string>> Handle(RevealLessorIdNumberQuery request, CancellationToken cancellationToken)
    {
        if (!await SensitiveDataAccess.CanViewAsync(db, currentUser, cancellationToken))
            return IdentityErrors.SensitiveDataForbidden;

        var encrypted = await db.Properties.AsNoTracking()
            .Where(p => p.Id == request.PropertyId)
            .Select(p => new { p.LessorIdNumberEncrypted })
            .FirstOrDefaultAsync(cancellationToken);
        if (encrypted is null)
            return PropertyErrors.PropertyNotFound;
        if (encrypted.LessorIdNumberEncrypted is null)
            return string.Empty;

        auditTrail.RecordRead(AuditActions.RevealIdNumber, nameof(Property), request.PropertyId);
        return protector.Decrypt(encrypted.LessorIdNumberEncrypted);
    }
}

// ============================================================ Ngân hàng & nội quy

/// <summary>Cả 3 trường null = xóa tài khoản ngân hàng.</summary>
public sealed record UpdateBankAccountCommand(Guid PropertyId, string? BankName, string? AccountNo, string? AccountName)
    : IRequest<Result<PropertyDetailDto>>;

public sealed class UpdateBankAccountCommandValidator : AbstractValidator<UpdateBankAccountCommand>
{
    public UpdateBankAccountCommandValidator()
    {
        When(x => x.BankName is not null || x.AccountNo is not null || x.AccountName is not null, () =>
        {
            RuleFor(x => x.BankName!).RequiredText(100, "Tên ngân hàng");
            RuleFor(x => x.AccountName!).RequiredText(200, "Tên chủ tài khoản");
            RuleFor(x => x.AccountNo)
                .NotEmpty().WithErrorCode("REQUIRED")
                .Matches(@"^\d{6,20}$").WithErrorCode("INVALID_BANK_ACCOUNT").WithMessage("Số tài khoản gồm 6–20 chữ số.");
        });
    }
}

public sealed class UpdateBankAccountHandler(IAppDbContext db, TimeProvider clock)
    : IRequestHandler<UpdateBankAccountCommand, Result<PropertyDetailDto>>
{
    public async ValueTask<Result<PropertyDetailDto>> Handle(UpdateBankAccountCommand request, CancellationToken cancellationToken)
    {
        var property = await db.Properties.FirstOrDefaultAsync(p => p.Id == request.PropertyId, cancellationToken);
        if (property is null)
            return PropertyErrors.PropertyNotFound;

        property.UpdateBankAccount(request.AccountNo is null
            ? null
            : new BankAccount(request.BankName!, request.AccountNo, request.AccountName!));
        await db.SaveChangesAsync(cancellationToken);
        return property.ToDetail(clock.GetUtcNow().ToBusinessDate());
    }
}

public sealed record UpdateHouseRulesCommand(Guid PropertyId, string? Text) : IRequest<Result<PropertyDetailDto>>;

public sealed class UpdateHouseRulesCommandValidator : AbstractValidator<UpdateHouseRulesCommand>
{
    public UpdateHouseRulesCommandValidator() => RuleFor(x => x.Text).OptionalText(20_000);
}

public sealed class UpdateHouseRulesHandler(IAppDbContext db, TimeProvider clock)
    : IRequestHandler<UpdateHouseRulesCommand, Result<PropertyDetailDto>>
{
    public async ValueTask<Result<PropertyDetailDto>> Handle(UpdateHouseRulesCommand request, CancellationToken cancellationToken)
    {
        var property = await db.Properties.FirstOrDefaultAsync(p => p.Id == request.PropertyId, cancellationToken);
        if (property is null)
            return PropertyErrors.PropertyNotFound;

        property.UpdateHouseRules(request.Text);
        await db.SaveChangesAsync(cancellationToken);
        return property.ToDetail(clock.GetUtcNow().ToBusinessDate());
    }
}

internal static class LessorErrors
{
    public static readonly Error IdNumberRequired =
        Error.Validation("ID_NUMBER_REQUIRED", "Đổi loại giấy tờ thì phải nhập lại số giấy tờ.");
}
