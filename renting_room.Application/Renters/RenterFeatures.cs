using FluentValidation;
using Mediator;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using renting_room.Application.Common.Interfaces;
using renting_room.Application.Common.Security;
using renting_room.Application.Common.Models;
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
    IdDocumentType IdType,
    string IdNumberMasked,
    DateOnly? IdIssueDate,
    string? IdIssuePlace,
    string? PermanentAddress,
    string? Occupation,
    string? Workplace,
    string? EmergencyContactName,
    string? EmergencyContactPhone,
    string? Note,
    DateTimeOffset CreatedAt,
    string Version);

internal static class RenterProjection
{
    public static IQueryable<RenterDto> ToDto(this IQueryable<Renter> renters) =>
        renters.Select(r => new RenterDto(
            r.Id, r.FullName, r.DateOfBirth, r.Gender, r.Phone, r.Email, r.Nationality, r.IdType,
            "********" + r.IdNumberLast4,
            r.IdIssueDate, r.IdIssuePlace, r.PermanentAddress, r.Occupation, r.Workplace,
            r.EmergencyContactName, r.EmergencyContactPhone, r.Note, r.CreatedAt, r.Version.ToString()));

    /// <summary>SĐT người thuê: di động VN chuẩn hóa, hoặc số quốc tế dạng +[mã nước][số] cho người nước ngoài.</summary>
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
    IdDocumentType IdType,
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
        RuleFor(x => x.IdType).IsInEnum();
        RuleFor(x => x.Phone)
            .Must(p => p is null || RenterProjection.NormalizePhone(p) is not null)
            .WithErrorCode("INVALID_PHONE").WithMessage("Số điện thoại không hợp lệ (di động VN hoặc dạng +mã nước).");
        RuleFor(x => x.Email).ValidEmail();
        RuleFor(x => x.Nationality)
            .Matches("^[A-Za-z]{2}$").When(x => x.Nationality is not null)
            .WithErrorCode("INVALID_NATIONALITY").WithMessage("Quốc tịch theo mã ISO 2 chữ cái (VD: VN, KR).");
        if (idNumberRequired)
            RuleFor(x => x.IdNumber!).IdNumber(x => x.IdType);
        else
            When(x => x.IdNumber is not null, () => RuleFor(x => x.IdNumber!).IdNumber(x => x.IdType));
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

// ============================================================ Tạo / sửa

public sealed record CreateRenterCommand(RenterInput Renter) : IRequest<Result<Guid>>;

public sealed class CreateRenterCommandValidator : AbstractValidator<CreateRenterCommand>
{
    // POST /renters gửi hồ sơ ở cấp gốc body (khác PUT bọc trong "renter").
    public CreateRenterCommandValidator(TimeProvider clock)
    {
        RuleFor(x => x.Renter).NotNull();
        RuleFor(x => x.Renter).FlattenedValidator(new RenterInputValidator(clock, idNumberRequired: true));
    }
}

/// <summary>RT-BR-02: cùng số giấy tờ trong tổ chức ⇒ 409, dùng lại hồ sơ cũ (unique index là chốt chặn khi gửi song song).</summary>
public sealed class CreateRenterHandler(IAppDbContext db, ICurrentUser currentUser, IPersonalDataProtector protector)
    : IRequestHandler<CreateRenterCommand, Result<Guid>>
{
    public async ValueTask<Result<Guid>> Handle(CreateRenterCommand request, CancellationToken cancellationToken)
    {
        var idNumber = protector.ProtectIdNumber(currentUser.OrganizationId!.Value, request.Renter.IdType, request.Renter.IdNumber!);
        var existingId = await db.Renters.Where(r => r.IdNumberHash == idNumber.Hash).Select(r => (Guid?)r.Id).FirstOrDefaultAsync(cancellationToken);
        if (existingId is { } existing)
            return RenterErrors.IdNumberExistsFor(existing);

        var renter = Renter.Create(request.Renter.ToProfile(), idNumber);
        db.Renters.Add(renter);
        await db.SaveChangesAsync(cancellationToken);
        return renter.Id;
    }
}

public sealed record UpdateRenterCommand(Guid Id, RenterInput Renter, uint Version) : IRequest<Result<RenterDto>>;

public sealed class UpdateRenterCommandValidator : AbstractValidator<UpdateRenterCommand>
{
    public UpdateRenterCommandValidator(TimeProvider clock) =>
        RuleFor(x => x.Renter).NotNull().SetValidator(new RenterInputValidator(clock, idNumberRequired: false));
}

public sealed class UpdateRenterHandler(IAppDbContext db, ICurrentUser currentUser, IPersonalDataProtector protector)
    : IRequestHandler<UpdateRenterCommand, Result<RenterDto>>
{
    public async ValueTask<Result<RenterDto>> Handle(UpdateRenterCommand request, CancellationToken cancellationToken)
    {
        var renter = await db.Renters.FirstOrDefaultAsync(r => r.Id == request.Id, cancellationToken);
        if (renter is null)
            return RenterErrors.RenterNotFound;

        if (request.Renter.IdNumber is null && request.Renter.IdType != renter.IdType)
            return Error.Validation("ID_NUMBER_REQUIRED", "Đổi loại giấy tờ thì phải nhập lại số giấy tờ.");

        var idNumber = request.Renter.IdNumber is null
            ? new ProtectedIdNumber(renter.IdType, renter.IdNumberEncrypted, renter.IdNumberHash, renter.IdNumberLast4)
            : protector.ProtectIdNumber(currentUser.OrganizationId!.Value, request.Renter.IdType, request.Renter.IdNumber);

        if (idNumber.Hash != renter.IdNumberHash
            && await db.Renters.Where(r => r.IdNumberHash == idNumber.Hash && r.Id != renter.Id)
                .Select(r => (Guid?)r.Id).FirstOrDefaultAsync(cancellationToken) is { } existing)
            return RenterErrors.IdNumberExistsFor(existing);

        db.SetExpectedVersion(renter, request.Version);
        renter.Update(request.Renter.ToProfile(), idNumber);
        await db.SaveChangesAsync(cancellationToken);

        return await db.Renters.AsNoTracking().Where(r => r.Id == renter.Id).ToDto().FirstAsync(cancellationToken);
    }
}

// ============================================================ Queries

public sealed record SearchRentersQuery(string? Q, string? IdNumber, IdDocumentType? IdType, int Page = 1, int PageSize = Paging.DefaultPageSize)
    : IRequest<PagedResult<RenterDto>>;

public sealed class SearchRentersQueryValidator : AbstractValidator<SearchRentersQuery>
{
    public SearchRentersQueryValidator()
    {
        RuleFor(x => x.Page).Page();
        RuleFor(x => x.PageSize).PageSize();
        RuleFor(x => x.Q).OptionalText(100);
        RuleFor(x => x.IdNumber).OptionalText(30);
    }
}

/// <summary>Tìm theo tên (không dấu) / SĐT; số giấy tờ tìm CHÍNH XÁC qua hash (không quét dữ liệu đã mã hóa).</summary>
public sealed class SearchRentersHandler(IAppDbContext db, ICurrentUser currentUser, IPersonalDataProtector protector)
    : IRequestHandler<SearchRentersQuery, PagedResult<RenterDto>>
{
    public async ValueTask<PagedResult<RenterDto>> Handle(SearchRentersQuery request, CancellationToken cancellationToken)
    {
        var query = db.Renters.AsNoTracking().Where(r => r.ArchivedAt == null);

        if (!string.IsNullOrWhiteSpace(request.IdNumber))
        {
            // Hash gồm cả loại giấy tờ ⇒ không chọn loại thì thử mọi loại (hộ chiếu người nước ngoài vẫn tìm được).
            var types = request.IdType is { } type ? [type] : Enum.GetValues<IdDocumentType>();
            var hashes = types
                .Select(t => protector.ProtectIdNumber(currentUser.OrganizationId!.Value, t, request.IdNumber).Hash)
                .ToList();
            query = query.Where(r => hashes.Contains(r.IdNumberHash));
        }

        if (!string.IsNullOrWhiteSpace(request.Q))
        {
            var text = TextNormalizer.ToSearchText(request.Q);
            var phone = RenterProjection.NormalizePhone(request.Q);
            query = query.Where(r => r.FullNameSearch.Contains(text) || (phone != null && r.Phone == phone));
        }

        var total = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderBy(r => r.FullNameSearch)
            .Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize)
            .ToDto()
            .ToListAsync(cancellationToken);

        return new PagedResult<RenterDto>(items, request.Page, request.PageSize, total);
    }
}

public sealed record GetRenterQuery(Guid Id) : IRequest<Result<RenterDto>>;

public sealed class GetRenterHandler(IAppDbContext db) : IRequestHandler<GetRenterQuery, Result<RenterDto>>
{
    public async ValueTask<Result<RenterDto>> Handle(GetRenterQuery request, CancellationToken cancellationToken)
    {
        var renter = await db.Renters.AsNoTracking().Where(r => r.Id == request.Id).ToDto().FirstOrDefaultAsync(cancellationToken);
        return renter is null ? RenterErrors.RenterNotFound : renter;
    }
}

/// <summary>Xem số giấy tờ đầy đủ — chỉ người có quyền (ID-BR-22), luôn ghi log ai xem (C-10).</summary>
public sealed record RevealRenterIdNumberQuery(Guid Id) : IRequest<Result<string>>;

public sealed class RevealRenterIdNumberHandler(
    IAppDbContext db, ICurrentUser currentUser, IPersonalDataProtector protector, ILogger<RevealRenterIdNumberHandler> logger)
    : IRequestHandler<RevealRenterIdNumberQuery, Result<string>>
{
    public async ValueTask<Result<string>> Handle(RevealRenterIdNumberQuery request, CancellationToken cancellationToken)
    {
        if (!await SensitiveDataAccess.CanViewAsync(db, currentUser, cancellationToken))
            return IdentityErrors.SensitiveDataForbidden;

        var encrypted = await db.Renters.AsNoTracking()
            .Where(r => r.Id == request.Id).Select(r => r.IdNumberEncrypted).FirstOrDefaultAsync(cancellationToken);
        if (encrypted is null)
            return RenterErrors.RenterNotFound;

        logger.LogWarning("AUDIT: user {UserId} revealed id number of renter {RenterId}", currentUser.UserId, request.Id);
        return protector.Decrypt(encrypted);
    }
}
