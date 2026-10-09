using FluentValidation;
using Mediator;
using Microsoft.EntityFrameworkCore;
using renting_room.Application.Common.Interfaces;
using renting_room.Application.Common.Models;
using renting_room.Application.Common.Validation;
using renting_room.Domain.Billing;
using renting_room.Domain.Common;
using renting_room.Domain.Contracts;
using renting_room.Domain.Fees;
using renting_room.Domain.Properties;

namespace renting_room.Application.Properties;

// ============================================================ Validators dùng chung

internal static class PropertyRules
{
    public static void AddressRules<T>(this AbstractValidator<T> v, Func<T, AddressInput> address)
    {
        v.RuleFor(x => address(x)).NotNull().OverridePropertyName("address").WithErrorCode("REQUIRED");
        v.When(x => address(x) is not null, () =>
        {
            v.RuleFor(x => address(x).StreetAddress).RequiredText(300, "Số nhà, đường").OverridePropertyName("address.streetAddress");
            v.RuleFor(x => address(x).CommuneName).RequiredText(100, "Xã/phường").OverridePropertyName("address.communeName");
            v.RuleFor(x => address(x).ProvinceName).RequiredText(100, "Tỉnh/thành phố").OverridePropertyName("address.provinceName");
            v.RuleFor(x => address(x).CommuneCode).OptionalText(10).OverridePropertyName("address.communeCode");
            v.RuleFor(x => address(x).ProvinceCode).OptionalText(10).OverridePropertyName("address.provinceCode");
        });
    }

    /// <summary>PR-BR-09: ngày chốt 1–28 (tháng nào cũng có ngày chốt).</summary>
    public static void BillingRules<T>(this AbstractValidator<T> v, Func<T, PropertyBillingInput?> billing)
    {
        v.When(x => billing(x) is not null, () =>
        {
            v.RuleFor(x => billing(x)!.AnchorDay).InclusiveBetween(BillingSchedule.MinAnchorDay, BillingSchedule.MaxAnchorDay)
                .OverridePropertyName("billing.anchorDay").WithErrorCode("OUT_OF_RANGE");
            v.RuleFor(x => billing(x)!.PaymentDueDays).InclusiveBetween(0, 60).OverridePropertyName("billing.paymentDueDays").WithErrorCode("OUT_OF_RANGE");
            v.RuleFor(x => billing(x)!.NoticeDays).InclusiveBetween(0, 180).OverridePropertyName("billing.noticeDays").WithErrorCode("OUT_OF_RANGE");
            v.RuleFor(x => billing(x)!.ChargeMode).IsInEnum().OverridePropertyName("billing.chargeMode");
            v.RuleFor(x => billing(x)!.ProrationMode).IsInEnum().OverridePropertyName("billing.prorationMode");
        });
    }

    public static void LandRules<T>(this AbstractValidator<T> v, Func<T, LandParcelInput?> land)
    {
        v.When(x => land(x) is not null, () =>
        {
            v.RuleFor(x => land(x)!.ParcelNo).OptionalText(20).OverridePropertyName("land.parcelNo");
            v.RuleFor(x => land(x)!.MapSheetNo).OptionalText(20).OverridePropertyName("land.mapSheetNo");
            v.RuleFor(x => land(x)!.OwnershipCertificateNo).OptionalText(50).OverridePropertyName("land.ownershipCertificateNo");
        });
    }
}

// ============================================================ Tạo / sửa khu

public sealed record CreatePropertyCommand(
    string Code,
    string Name,
    AddressInput Address,
    string? Description,
    string? EvnCustomerCode,
    LandParcelInput? Land,
    PropertyBillingInput? Billing) : IRequest<Result<Guid>>;

public sealed class CreatePropertyCommandValidator : AbstractValidator<CreatePropertyCommand>
{
    public CreatePropertyCommandValidator()
    {
        RuleFor(x => x.Code).Code(32, "Mã khu trọ");
        RuleFor(x => x.Name).RequiredText(200, "Tên khu trọ");
        RuleFor(x => x.Description).OptionalText(2000);
        RuleFor(x => x.EvnCustomerCode).OptionalText(20);
        this.AddressRules(x => x.Address);
        this.BillingRules(x => x.Billing);
        this.LandRules(x => x.Land);
    }
}

public sealed class CreatePropertyHandler(IAppDbContext db) : IRequestHandler<CreatePropertyCommand, Result<Guid>>
{
    public async ValueTask<Result<Guid>> Handle(CreatePropertyCommand request, CancellationToken cancellationToken)
    {
        var code = TextNormalizer.NormalizeCode(request.Code);
        if (await db.Properties.AnyAsync(p => p.Code == code, cancellationToken))
            return PropertyErrors.PropertyCodeTaken;

        var property = Property.Create(
            code, request.Name, request.Address.ToDomain(), request.Billing?.ToDomain() ?? BillingSettings.Standard);
        property.UpdateInfo(request.Name, request.Address.ToDomain(), request.Description, request.EvnCustomerCode, request.Land.ToDomain());

        db.Properties.Add(property);
        // FE-UC-01: khu mới có sẵn Điện + Nước theo công tơ (chưa có giá), tự gắn vào HĐ mới.
        db.FeeTypes.AddRange(FeeType.DefaultsFor(property.Id));
        await db.SaveChangesAsync(cancellationToken);
        return property.Id;
    }
}

public sealed record UpdatePropertyCommand(
    Guid Id,
    string Name,
    AddressInput Address,
    string? Description,
    string? EvnCustomerCode,
    LandParcelInput? Land,
    uint Version) : IRequest<Result<PropertyDetailDto>>;

public sealed class UpdatePropertyCommandValidator : AbstractValidator<UpdatePropertyCommand>
{
    public UpdatePropertyCommandValidator()
    {
        RuleFor(x => x.Name).RequiredText(200, "Tên khu trọ");
        RuleFor(x => x.Description).OptionalText(2000);
        RuleFor(x => x.EvnCustomerCode).OptionalText(20);
        this.AddressRules(x => x.Address);
        this.LandRules(x => x.Land);
    }
}

/// <summary>Sửa thông tin khu. Cài đặt kỳ thu đổi qua <see cref="UpdatePropertyBillingCommand"/> (K4).</summary>
public sealed class UpdatePropertyHandler(IAppDbContext db, TimeProvider clock) : IRequestHandler<UpdatePropertyCommand, Result<PropertyDetailDto>>
{
    public async ValueTask<Result<PropertyDetailDto>> Handle(UpdatePropertyCommand request, CancellationToken cancellationToken)
    {
        var property = await db.Properties.FirstOrDefaultAsync(p => p.Id == request.Id, cancellationToken);
        if (property is null)
            return PropertyErrors.PropertyNotFound;

        db.SetExpectedVersion(property, request.Version);
        property.UpdateInfo(request.Name, request.Address.ToDomain(), request.Description, request.EvnCustomerCode, request.Land.ToDomain());
        await db.SaveChangesAsync(cancellationToken);

        return await property.ToDetailAsync(db, clock.GetUtcNow().ToBusinessDate(), cancellationToken);
    }
}

// ============================================================ Ngừng dùng / khôi phục

public sealed record ArchivePropertyCommand(Guid Id) : IRequest<Result>;

/// <summary>PR-BR-05: chỉ khi khu không còn hợp đồng nháp/hiệu lực/thanh lý; các phòng được ngừng dùng cùng lúc.</summary>
public sealed class ArchivePropertyHandler(IAppDbContext db, TimeProvider clock) : IRequestHandler<ArchivePropertyCommand, Result>
{
    public async ValueTask<Result> Handle(ArchivePropertyCommand request, CancellationToken cancellationToken)
    {
        await using var transaction = await db.BeginTransactionAsync(cancellationToken);
        await db.LockForUpdateAsync<Property>(request.Id, cancellationToken);

        var property = await db.Properties.FirstOrDefaultAsync(p => p.Id == request.Id, cancellationToken);
        if (property is null)
            return Result.Failure(PropertyErrors.PropertyNotFound);

        // Kể cả HĐ đã kết thúc mà hôm nay là ngày trả phòng (người thuê còn ở hết ngày).
        var today = clock.GetUtcNow().ToBusinessDate();
        var hasOpenContracts = await db.Contracts.AnyAsync(c => c.PropertyId == property.Id
            && (c.Status == ContractStatus.Draft || c.Status == ContractStatus.Active || c.Status == ContractStatus.Liquidating
                || (c.Status == ContractStatus.Ended && c.ActualEndDate >= today)),
            cancellationToken);
        if (hasOpenContracts)
            return Result.Failure(PropertyErrors.PropertyHasActiveContracts);

        var now = clock.GetUtcNow();
        var archived = property.Archive(now);
        if (archived.IsFailure)
            return archived;

        var rooms = await db.Rooms.Where(r => r.PropertyId == property.Id && r.ArchivedAt == null).ToListAsync(cancellationToken);
        rooms.ForEach(r => r.Archive(now));

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return Result.Success();
    }
}

public sealed record RestorePropertyCommand(Guid Id) : IRequest<Result>;

public sealed class RestorePropertyHandler(IAppDbContext db) : IRequestHandler<RestorePropertyCommand, Result>
{
    public async ValueTask<Result> Handle(RestorePropertyCommand request, CancellationToken cancellationToken)
    {
        var property = await db.Properties.FirstOrDefaultAsync(p => p.Id == request.Id, cancellationToken);
        if (property is null)
            return Result.Failure(PropertyErrors.PropertyNotFound);

        var restored = property.Restore();
        if (restored.IsFailure)
            return restored;

        await db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}

// ============================================================ Queries

public sealed record ListPropertiesQuery(string? Search, bool IncludeArchived, int Page = 1, int PageSize = Paging.DefaultPageSize)
    : IRequest<PagedResult<PropertySummaryDto>>;

public sealed class ListPropertiesQueryValidator : AbstractValidator<ListPropertiesQuery>
{
    public ListPropertiesQueryValidator()
    {
        RuleFor(x => x.Page).Page();
        RuleFor(x => x.PageSize).PageSize();
        RuleFor(x => x.Search).OptionalText(100);
    }
}

public sealed class ListPropertiesHandler(IAppDbContext db, TimeProvider clock)
    : IRequestHandler<ListPropertiesQuery, PagedResult<PropertySummaryDto>>
{
    public async ValueTask<PagedResult<PropertySummaryDto>> Handle(ListPropertiesQuery request, CancellationToken cancellationToken)
    {
        var today = clock.GetUtcNow().ToBusinessDate();
        var query = db.Properties.AsNoTracking();
        if (!request.IncludeArchived)
            query = query.Where(p => p.ArchivedAt == null);
        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var term = request.Search.Trim().ToUpper();
            query = query.Where(p => p.Code.Contains(term) || p.Name.ToUpper().Contains(term));
        }

        var total = await query.CountAsync(cancellationToken);
        var rows = await query
            .OrderBy(p => p.Code)
            .Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize)
            .Select(p => new
            {
                Property = p,
                RoomCount = db.Rooms.Count(r => r.PropertyId == p.Id && r.ArchivedAt == null),
                OccupiedRoomCount = db.Contracts
                    .Where(c => c.PropertyId == p.Id
                        && (c.Status == ContractStatus.Active || c.Status == ContractStatus.Liquidating)
                        && c.StartDate <= today && (c.ActualEndDate == null || c.ActualEndDate >= today))
                    .Select(c => c.RoomId).Distinct().Count()
            })
            .ToListAsync(cancellationToken);

        // PR-BR-17: khu không khai bên cho thuê riêng ⇒ xét thông tin chủ trọ.
        var organizationLessorComplete = rows.Count > 0
            && (await LessorSource.OrganizationLessorAsync(db, rows[0].Property.OrganizationId, cancellationToken))?.IsComplete(today) == true;
        var items = rows.Select(r => new PropertySummaryDto(
                r.Property.Id, r.Property.Code, r.Property.Name, r.Property.Address.FullText, r.RoomCount, r.OccupiedRoomCount,
                r.Property.Lessor?.IsComplete(today) ?? organizationLessorComplete, r.Property.IsArchived))
            .ToList();

        return new PagedResult<PropertySummaryDto>(items, request.Page, request.PageSize, total);
    }
}

public sealed record GetPropertyQuery(Guid Id) : IRequest<Result<PropertyDetailDto>>;

public sealed class GetPropertyHandler(IAppDbContext db, TimeProvider clock) : IRequestHandler<GetPropertyQuery, Result<PropertyDetailDto>>
{
    public async ValueTask<Result<PropertyDetailDto>> Handle(GetPropertyQuery request, CancellationToken cancellationToken)
    {
        var property = await db.Properties.AsNoTracking().FirstOrDefaultAsync(p => p.Id == request.Id, cancellationToken);
        return property is null
            ? PropertyErrors.PropertyNotFound
            : await property.ToDetailAsync(db, clock.GetUtcNow().ToBusinessDate(), cancellationToken);
    }
}
