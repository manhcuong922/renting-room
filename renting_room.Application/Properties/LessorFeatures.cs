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

// ============================================================ Bên cho thuê (PR-BR-12 → 17)

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

/// <summary>Khai bên cho thuê <b>riêng</b> cho khu — chỉ khi khác chủ trọ (công ty, người được ủy quyền…). Không khai ⇒ dùng thông tin chủ trọ.</summary>
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
    DateOnly? AuthorizationDocDate) : IRequest<Result<PropertyDetailDto>>, ILessorFields;

/// <summary>PR-BR-17: chủ trọ khai thông tin của mình một lần — mọi khu không khai riêng dùng thông tin này.</summary>
/// <param name="IdNumber">Null = giữ nguyên số giấy tờ đang lưu.</param>
public sealed record UpdateOrganizationLessorCommand(
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
    DateOnly? AuthorizationDocDate) : IRequest<Result<OrganizationLessorDto>>, ILessorFields;

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

/// <summary>PR-BR-17: bên cho thuê hiệu lực của khu = bên cho thuê riêng của khu, không có thì lấy thông tin chủ trọ (tổ chức).</summary>
internal static class LessorSource
{
    public static async Task<(LessorDetails? Lessor, bool Inherited)> EffectiveAsync(IAppDbContext db, Property property, CancellationToken ct) =>
        property.Lessor is { } own ? (own, false) : (await OrganizationLessorAsync(db, property.OrganizationId, ct), true);

    public static Task<LessorDetails?> OrganizationLessorAsync(IAppDbContext db, Guid organizationId, CancellationToken ct) =>
        db.Organizations.AsNoTracking().Where(o => o.Id == organizationId).Select(o => o.DefaultLessor).FirstOrDefaultAsync(ct);
}

public sealed class UpdateLessorCommandValidator : AbstractValidator<UpdateLessorCommand>
{
    public UpdateLessorCommandValidator(TimeProvider clock) => this.Lessor(clock);
}

public sealed class UpdateLessorHandler(IAppDbContext db, ICurrentUser currentUser, IPersonalDataProtector protector, TimeProvider clock)
    : IRequestHandler<UpdateLessorCommand, Result<PropertyDetailDto>>
{
    public async ValueTask<Result<PropertyDetailDto>> Handle(UpdateLessorCommand request, CancellationToken cancellationToken)
    {
        var property = await db.Properties.FirstOrDefaultAsync(p => p.Id == request.PropertyId, cancellationToken);
        if (property is null)
            return PropertyErrors.PropertyNotFound;

        var lessor = LessorRules.Build(request, property.Lessor, protector, currentUser.OrganizationId!.Value);
        if (lessor.IsFailure)
            return lessor.Error!;
        property.UpdateLessor(lessor.Value!);
        await db.SaveChangesAsync(cancellationToken);
        return await property.ToDetailAsync(db, clock.GetUtcNow().ToBusinessDate(), cancellationToken);
    }
}

/// <summary>Bỏ bên cho thuê riêng của khu ⇒ dùng lại thông tin chủ trọ (PR-BR-17). HĐ đã kích hoạt giữ bản chụp.</summary>
public sealed record ClearPropertyLessorCommand(Guid PropertyId) : IRequest<Result<PropertyDetailDto>>;

public sealed class ClearPropertyLessorHandler(IAppDbContext db, TimeProvider clock)
    : IRequestHandler<ClearPropertyLessorCommand, Result<PropertyDetailDto>>
{
    public async ValueTask<Result<PropertyDetailDto>> Handle(ClearPropertyLessorCommand request, CancellationToken cancellationToken)
    {
        var property = await db.Properties.FirstOrDefaultAsync(p => p.Id == request.PropertyId, cancellationToken);
        if (property is null)
            return PropertyErrors.PropertyNotFound;

        property.ClearLessor();
        await db.SaveChangesAsync(cancellationToken);
        return await property.ToDetailAsync(db, clock.GetUtcNow().ToBusinessDate(), cancellationToken);
    }
}

/// <param name="Prefill">Gợi ý điền form lần đầu từ thông tin liên hệ của tổ chức (tên, SĐT, địa chỉ chủ trọ).</param>
public sealed record OrganizationLessorDto(LessorDto? Lessor, LessorPrefillDto Prefill);

public sealed record LessorPrefillDto(string? Name, string? Phone, string? Address);

public sealed record GetOrganizationLessorQuery : IRequest<OrganizationLessorDto>;

public sealed class GetOrganizationLessorHandler(IAppDbContext db, ICurrentUser currentUser, TimeProvider clock)
    : IRequestHandler<GetOrganizationLessorQuery, OrganizationLessorDto>
{
    public async ValueTask<OrganizationLessorDto> Handle(GetOrganizationLessorQuery request, CancellationToken cancellationToken)
    {
        var organization = await db.Organizations.AsNoTracking().FirstAsync(o => o.Id == currentUser.OrganizationId, cancellationToken);
        return organization.ToLessorDto(clock.GetUtcNow().ToBusinessDate());
    }
}

public sealed class UpdateOrganizationLessorCommandValidator : AbstractValidator<UpdateOrganizationLessorCommand>
{
    public UpdateOrganizationLessorCommandValidator(TimeProvider clock) => this.Lessor(clock);
}

public sealed class UpdateOrganizationLessorHandler(IAppDbContext db, ICurrentUser currentUser, IPersonalDataProtector protector, TimeProvider clock)
    : IRequestHandler<UpdateOrganizationLessorCommand, Result<OrganizationLessorDto>>
{
    public async ValueTask<Result<OrganizationLessorDto>> Handle(UpdateOrganizationLessorCommand request, CancellationToken cancellationToken)
    {
        var organization = await db.Organizations.FirstAsync(o => o.Id == currentUser.OrganizationId, cancellationToken);
        var lessor = LessorRules.Build(request, organization.DefaultLessor, protector, organization.Id);
        if (lessor.IsFailure)
            return lessor.Error!;
        organization.UpdateDefaultLessor(lessor.Value!);
        await db.SaveChangesAsync(cancellationToken);
        return organization.ToLessorDto(clock.GetUtcNow().ToBusinessDate());
    }
}

/// <summary>
/// Xem số giấy tờ đầy đủ của bên cho thuê hiệu lực của khu (riêng của khu, không có thì của chủ trọ) — chỉ người có quyền (ID-BR-22),
/// luôn ghi log ai xem (C-10, ID-BR-21).
/// </summary>
public sealed record RevealLessorIdNumberQuery(Guid PropertyId) : IRequest<Result<string>>;

public sealed class RevealLessorIdNumberHandler(
    IAppDbContext db, ICurrentUser currentUser, IPersonalDataProtector protector, IAuditTrail auditTrail)
    : IRequestHandler<RevealLessorIdNumberQuery, Result<string>>
{
    public async ValueTask<Result<string>> Handle(RevealLessorIdNumberQuery request, CancellationToken cancellationToken)
    {
        if (!await SensitiveDataAccess.CanViewAsync(db, currentUser, cancellationToken))
            return IdentityErrors.SensitiveDataForbidden;

        var property = await db.Properties.AsNoTracking().FirstOrDefaultAsync(p => p.Id == request.PropertyId, cancellationToken);
        if (property is null)
            return PropertyErrors.PropertyNotFound;
        var (lessor, _) = await LessorSource.EffectiveAsync(db, property, cancellationToken);
        if (lessor?.IdNumberEncrypted is null)
            return string.Empty;

        auditTrail.RecordRead(AuditActions.RevealIdNumber, nameof(Property), request.PropertyId);
        return protector.Decrypt(lessor.IdNumberEncrypted);
    }
}

/// <summary>Xem số giấy tờ đầy đủ của chủ trọ (bên cho thuê mặc định) — như <see cref="RevealLessorIdNumberQuery"/>.</summary>
public sealed record RevealOrganizationLessorIdNumberQuery : IRequest<Result<string>>;

public sealed class RevealOrganizationLessorIdNumberHandler(
    IAppDbContext db, ICurrentUser currentUser, IPersonalDataProtector protector, IAuditTrail auditTrail)
    : IRequestHandler<RevealOrganizationLessorIdNumberQuery, Result<string>>
{
    public async ValueTask<Result<string>> Handle(RevealOrganizationLessorIdNumberQuery request, CancellationToken cancellationToken)
    {
        if (!await SensitiveDataAccess.CanViewAsync(db, currentUser, cancellationToken))
            return IdentityErrors.SensitiveDataForbidden;

        var organizationId = currentUser.OrganizationId!.Value;
        var lessor = await LessorSource.OrganizationLessorAsync(db, organizationId, cancellationToken);
        if (lessor?.IdNumberEncrypted is null)
            return string.Empty;

        auditTrail.RecordRead(AuditActions.RevealIdNumber, nameof(Organization), organizationId);
        return protector.Decrypt(lessor.IdNumberEncrypted);
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
        return await property.ToDetailAsync(db, clock.GetUtcNow().ToBusinessDate(), cancellationToken);
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
        return await property.ToDetailAsync(db, clock.GetUtcNow().ToBusinessDate(), cancellationToken);
    }
}

internal static class LessorErrors
{
    public static readonly Error IdNumberRequired =
        Error.Validation("ID_NUMBER_REQUIRED", "Đổi loại giấy tờ thì phải nhập lại số giấy tờ.");
}
