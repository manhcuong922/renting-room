using FluentValidation;
using Mediator;
using Microsoft.EntityFrameworkCore;
using renting_room.Application.Common.Interfaces;
using renting_room.Application.Common.Models;
using renting_room.Application.Common.Validation;
using renting_room.Domain.Common;
using renting_room.Domain.Contracts;
using renting_room.Domain.Fees;
using renting_room.Domain.Properties;

namespace renting_room.Application.Fees;

// ============================================================ DTO & input

/// <param name="Tiers">FE-BR-15: giá theo bậc (chỉ điện nước theo công tơ); null ⇒ một giá (<c>UnitPrice</c>).</param>
public sealed record FeePriceInput(DateOnly EffectiveFrom, decimal? UnitPrice, string? Note, IReadOnlyList<PriceTier>? Tiers = null);

public sealed record FeePriceDto(Guid Id, DateOnly EffectiveFrom, decimal UnitPrice, string? Note, IReadOnlyList<PriceTier>? Tiers);

public sealed record FeeTypeDto(
    Guid Id,
    Guid PropertyId,
    string Name,
    FeeGroup Group,
    ChargeBasis? ChargeBasis,
    string Unit,
    string? SystemCode,
    bool AutoAttach,
    decimal? DefaultQuantity,
    int SortOrder,
    VehicleType? VehicleType,
    bool IsArchived,
    FeePriceDto? CurrentPrice,
    IReadOnlyList<FeePriceDto> Prices,
    string Version)
{
    public static FeeTypeDto From(FeeType f, DateOnly today) => new(
        f.Id, f.PropertyId, f.Name, f.Group, f.ChargeBasis, f.Unit, f.SystemCode, f.AutoAttach, f.DefaultQuantity, f.SortOrder,
        f.VehicleType, f.IsArchived, f.ResolvePrice(today) is { } p ? ToDto(p) : null,
        f.Prices.OrderByDescending(p => p.EffectiveFrom).Select(ToDto).ToList(), f.Version.ToString());

    private static FeePriceDto ToDto(FeePrice p) => new(p.Id, p.EffectiveFrom, p.UnitPrice, p.Note, p.Tiers);
}

// ============================================================ Validation dùng chung

internal static class FeeRules
{
    public const decimal MaxMeteredPrice = 100_000;
    public const decimal MaxOtherPrice = 50_000_000;

    public static decimal MaxPrice(FeeGroup group) => group == FeeGroup.Metered ? MaxMeteredPrice : MaxOtherPrice;

    public static bool IsValidPrice(decimal price, FeeGroup group) =>
        price >= 0 && price <= MaxPrice(group) && decimal.Round(price, 2) == price;

    public const int MaxTiers = 10;

    /// <summary>Bậc tăng dần theo <c>UpTo</c>, chỉ bậc cuối để trống (không giới hạn); giá mỗi bậc như giá theo công tơ.</summary>
    public static bool IsValidTiers(IReadOnlyList<PriceTier> tiers) =>
        tiers.Count is > 0 and <= MaxTiers
        && tiers.Take(tiers.Count - 1).All(t => t.UpTo is > 0)
        && tiers[^1].UpTo is null
        && tiers.Take(tiers.Count - 1).Zip(tiers.Skip(1).Take(tiers.Count - 2)).All(p => p.First.UpTo < p.Second.UpTo)
        && tiers.All(t => IsValidPrice(t.Price, FeeGroup.Metered));

    public static void PriceRules<T>(this AbstractValidator<T> v, Func<T, FeePriceInput?> price, Func<T, FeeGroup?> group, string path, TimeProvider clock)
    {
        v.RuleFor(x => price(x)!.EffectiveFrom)
            .Must(d => d <= clock.GetUtcNow().ToBusinessDate().AddDays(366)).When(x => price(x) is not null)
            .OverridePropertyName($"{path}effectiveFrom").WithErrorCode("OUT_OF_RANGE").WithMessage("Ngày hiệu lực tối đa sau hôm nay 1 năm.");
        v.RuleFor(x => price(x)!.UnitPrice)
            .NotNull().When(x => price(x) is { Tiers: null or { Count: 0 } })
            .OverridePropertyName($"{path}unitPrice").WithErrorCode("REQUIRED").WithMessage("Nhập đơn giá (hoặc giá theo bậc).");
        v.RuleFor(x => price(x)!.Tiers)
            .Must(t => t is null || t.Count == 0 || IsValidTiers(t)).When(x => price(x) is not null)
            .OverridePropertyName($"{path}tiers").WithErrorCode("INVALID_TIERS")
            .WithMessage($"Giá theo bậc: 1–{MaxTiers} bậc, mốc \"upTo\" tăng dần, bậc cuối để trống; giá mỗi bậc 0–100.000đ.");
        v.RuleFor(x => price(x)!.UnitPrice)
            .Must((x, p) => p is null || IsValidPrice(p.Value, group(x) ?? FeeGroup.Service)).When(x => price(x) is not null)
            .OverridePropertyName($"{path}unitPrice").WithErrorCode("INVALID_AMOUNT")
            .WithMessage("Đơn giá ≥ 0, tối đa 2 số lẻ; theo chỉ số ≤ 100.000đ/đơn vị, loại khác ≤ 50 triệu (chặn nhập thừa số 0).");
        v.RuleFor(x => price(x)!.Note).OptionalText(300).When(x => price(x) is not null).OverridePropertyName($"{path}note");
    }
}

internal static class FeeWarnings
{
    public static IReadOnlyList<Warning> ForPrice(FeeType fee, FeePrice price, IFeeSettings settings)
    {
        return fee.SystemCode == FeeSystemCodes.Electricity && price.HighestPrice > settings.ElectricityPriceWarningThreshold
            ? [new Warning("ELECTRICITY_PRICE_ABOVE_THRESHOLD",
                $"Giá điện {price.HighestPrice:N0}đ/kWh cao hơn mức tham chiếu {settings.ElectricityPriceWarningThreshold:N0}đ — " +
                "tiền điện thu của người thuê không được vượt giá bán lẻ (Thông tư 60/2025/TT-BCT).")]
            : [];
    }
}

// ============================================================ Truy vấn

public sealed record ListFeeTypesQuery(Guid PropertyId, bool IncludeArchived) : IRequest<Result<IReadOnlyList<FeeTypeDto>>>;

public sealed class ListFeeTypesHandler(IAppDbContext db, TimeProvider clock) : IRequestHandler<ListFeeTypesQuery, Result<IReadOnlyList<FeeTypeDto>>>
{
    public async ValueTask<Result<IReadOnlyList<FeeTypeDto>>> Handle(ListFeeTypesQuery request, CancellationToken cancellationToken)
    {
        if (!await db.Properties.AnyAsync(p => p.Id == request.PropertyId, cancellationToken))
            return PropertyErrors.PropertyNotFound;

        var fees = await db.FeeTypes.AsNoTracking().Include(f => f.Prices)
            .Where(f => f.PropertyId == request.PropertyId && (request.IncludeArchived || f.ArchivedAt == null))
            .OrderBy(f => f.SortOrder).ThenBy(f => f.Name)
            .ToListAsync(cancellationToken);
        return fees.Select(f => FeeTypeDto.From(f, clock.GetUtcNow().ToBusinessDate())).ToList();
    }
}

public sealed record GetFeeTypeQuery(Guid Id) : IRequest<Result<FeeTypeDto>>;

public sealed class GetFeeTypeHandler(IAppDbContext db, TimeProvider clock) : IRequestHandler<GetFeeTypeQuery, Result<FeeTypeDto>>
{
    public async ValueTask<Result<FeeTypeDto>> Handle(GetFeeTypeQuery request, CancellationToken cancellationToken)
    {
        var fee = await db.FeeTypes.AsNoTracking().Include(f => f.Prices).FirstOrDefaultAsync(f => f.Id == request.Id, cancellationToken);
        return fee is null ? FeeErrors.NotFound : FeeTypeDto.From(fee, clock.GetUtcNow().ToBusinessDate());
    }
}

// ============================================================ Tạo / sửa

public sealed record CreateFeeTypeCommand(
    Guid PropertyId,
    string Name,
    FeeGroup Group,
    ChargeBasis? ChargeBasis,
    string Unit,
    bool AutoAttach,
    decimal? DefaultQuantity,
    int? SortOrder,
    FeePriceInput? InitialPrice,
    VehicleType? VehicleType = null) : IRequest<Result<CreatedWithWarnings>>;

public sealed class CreateFeeTypeCommandValidator : AbstractValidator<CreateFeeTypeCommand>
{
    public CreateFeeTypeCommandValidator(TimeProvider clock)
    {
        RuleFor(x => x.Name).RequiredText(100, "Tên khoản thu");
        RuleFor(x => x.Unit).RequiredText(20, "Đơn vị");
        RuleFor(x => x.Group).IsInEnum();
        RuleFor(x => x.ChargeBasis).NotNull().When(x => x.Group == FeeGroup.Service)
            .WithErrorCode("REQUIRED").WithMessage("Chọn cách tính dịch vụ: theo phòng, theo đầu người hay theo số gói.");
        RuleFor(x => x.ChargeBasis).Null().When(x => x.Group != FeeGroup.Service)
            .WithErrorCode("INVALID_CHARGE_BASIS").WithMessage("Điện nước theo công tơ không chọn cách tính dịch vụ.");
        RuleFor(x => x.ChargeBasis).IsInEnum().When(x => x.ChargeBasis is not null);
        RuleFor(x => x.DefaultQuantity)
            .Must(q => q is null || (q > 0 && q <= FeeType.MaxQuantity && decimal.Round(q.Value, 2) == q))
            .WithErrorCode("OUT_OF_RANGE").WithMessage("Số lượng mặc định 0–100, tối đa 2 số lẻ.");
        RuleFor(x => x.DefaultQuantity).Null().When(x => x.ChargeBasis != ChargeBasis.PerUnit)
            .WithErrorCode("INVALID_DEFAULT_QUANTITY").WithMessage("Chỉ dịch vụ theo số gói mới có số gói mặc định.");
        RuleFor(x => x.SortOrder).InclusiveBetween(0, 1000).When(x => x.SortOrder is not null).WithErrorCode("OUT_OF_RANGE");
        RuleFor(x => x.VehicleType).IsInEnum().When(x => x.VehicleType is not null);
        RuleFor(x => x.VehicleType).Null().When(x => x.ChargeBasis != ChargeBasis.PerUnit)
            .WithErrorCode("INVALID_VEHICLE_TYPE").WithMessage("Chỉ dịch vụ theo số gói (phí giữ xe) mới gắn loại xe.");
        this.PriceRules(x => x.InitialPrice, x => x.Group, "initialPrice.", clock);
    }
}

public sealed class CreateFeeTypeHandler(IAppDbContext db, IFeeSettings settings) : IRequestHandler<CreateFeeTypeCommand, Result<CreatedWithWarnings>>
{
    public async ValueTask<Result<CreatedWithWarnings>> Handle(CreateFeeTypeCommand request, CancellationToken cancellationToken)
    {
        var property = await db.Properties.AsNoTracking().FirstOrDefaultAsync(p => p.Id == request.PropertyId, cancellationToken);
        if (property is null)
            return PropertyErrors.PropertyNotFound;
        if (property.IsArchived)
            return PropertyErrors.PropertyArchived;

        var normalized = FeeType.NormalizeName(request.Name);
        if (await db.FeeTypes.AnyAsync(f => f.PropertyId == request.PropertyId && f.NameNormalized == normalized && f.ArchivedAt == null, cancellationToken))
            return FeeErrors.NameTaken;

        var fee = FeeType.Create(request.PropertyId, request.Name, request.Group, request.ChargeBasis, request.Unit, request.AutoAttach,
            request.DefaultQuantity, request.SortOrder ?? 100, vehicleType: request.VehicleType);
        IReadOnlyList<Warning> warnings = [];
        if (request.InitialPrice is { } price)
        {
            var added = fee.AddPrice(price.EffectiveFrom, price.UnitPrice ?? 0, price.Note, lockedUntil: null, price.Tiers);
            if (added.IsFailure)
                return added.Error!;
            warnings = FeeWarnings.ForPrice(fee, added.Value!, settings);
        }

        db.FeeTypes.Add(fee);
        await db.SaveChangesAsync(cancellationToken);
        return new CreatedWithWarnings(fee.Id, warnings);
    }
}

public sealed record UpdateFeeTypeCommand(
    Guid Id, string Name, string Unit, bool AutoAttach, decimal? DefaultQuantity, int SortOrder, uint Version, VehicleType? VehicleType = null)
    : IRequest<Result<FeeTypeDto>>;

public sealed class UpdateFeeTypeCommandValidator : AbstractValidator<UpdateFeeTypeCommand>
{
    public UpdateFeeTypeCommandValidator()
    {
        RuleFor(x => x.Name).RequiredText(100, "Tên khoản thu");
        RuleFor(x => x.Unit).RequiredText(20, "Đơn vị");
        RuleFor(x => x.DefaultQuantity)
            .Must(q => q is null || (q > 0 && q <= FeeType.MaxQuantity && decimal.Round(q.Value, 2) == q))
            .WithErrorCode("OUT_OF_RANGE").WithMessage("Số lượng mặc định 0–100, tối đa 2 số lẻ.");
        RuleFor(x => x.SortOrder).InclusiveBetween(0, 1000).WithErrorCode("OUT_OF_RANGE");
    }
}

/// <summary>FE-BR-02: không đổi nhóm / cách tính — muốn đổi thì tạo khoản mới và ngừng dùng khoản cũ.</summary>
public sealed class UpdateFeeTypeHandler(IAppDbContext db, TimeProvider clock) : IRequestHandler<UpdateFeeTypeCommand, Result<FeeTypeDto>>
{
    public async ValueTask<Result<FeeTypeDto>> Handle(UpdateFeeTypeCommand request, CancellationToken cancellationToken)
    {
        var fee = await db.FeeTypes.Include(f => f.Prices).FirstOrDefaultAsync(f => f.Id == request.Id, cancellationToken);
        if (fee is null)
            return FeeErrors.NotFound;
        if (!fee.IsPerUnit && (request.DefaultQuantity is not null || request.VehicleType is not null))
            return Error.Validation("INVALID_DEFAULT_QUANTITY", "Chỉ dịch vụ theo số gói mới có số gói mặc định / loại xe.");

        var normalized = FeeType.NormalizeName(request.Name);
        if (!fee.IsArchived && await db.FeeTypes.AnyAsync(f => f.PropertyId == fee.PropertyId && f.NameNormalized == normalized
                && f.ArchivedAt == null && f.Id != fee.Id, cancellationToken))
            return FeeErrors.NameTaken;

        db.SetExpectedVersion(fee, request.Version);
        fee.Update(request.Name, request.Unit, request.AutoAttach, request.DefaultQuantity, request.SortOrder, request.VehicleType);
        await db.SaveChangesAsync(cancellationToken);
        return FeeTypeDto.From(fee, clock.GetUtcNow().ToBusinessDate());
    }
}

// ============================================================ Ngừng dùng / khôi phục

public sealed record ChangeFeeTypeStateCommand(Guid Id, bool Archive) : IRequest<Result>;

/// <summary>Ngừng dùng chỉ khi không còn HĐ nháp / hiệu lực / thanh lý đang gắn khoản này (thay FE-BR-12 bản đầu).</summary>
public sealed class ChangeFeeTypeStateHandler(IAppDbContext db, TimeProvider clock) : IRequestHandler<ChangeFeeTypeStateCommand, Result>
{
    public async ValueTask<Result> Handle(ChangeFeeTypeStateCommand request, CancellationToken cancellationToken)
    {
        var fee = await db.FeeTypes.FirstOrDefaultAsync(f => f.Id == request.Id, cancellationToken);
        if (fee is null)
            return Result.Failure(FeeErrors.NotFound);

        if (request.Archive)
        {
            var today = clock.GetUtcNow().ToBusinessDate();
            var inUse = await db.Contracts
                .Where(c => c.Status == ContractStatus.Draft || c.Status == ContractStatus.Active || c.Status == ContractStatus.Liquidating)
                .SelectMany(c => c.Fees)
                .AnyAsync(f => f.FeeTypeId == fee.Id && (f.EffectiveTo == null || f.EffectiveTo >= today), cancellationToken);
            if (inUse)
                return Result.Failure(FeeErrors.InUse);
            if (fee.Group == FeeGroup.Metered && await db.Meters.AnyAsync(m => m.FeeTypeId == fee.Id && m.RemovedDate == null, cancellationToken))
                return Result.Failure(FeeErrors.HasActiveMeters);
            var archived = fee.Archive(clock.GetUtcNow());
            if (archived.IsFailure)
                return archived;
        }
        else
        {
            if (await db.FeeTypes.AnyAsync(f => f.PropertyId == fee.PropertyId && f.ArchivedAt == null
                    && (f.NameNormalized == fee.NameNormalized || (fee.SystemCode != null && f.SystemCode == fee.SystemCode)), cancellationToken))
                return Result.Failure(FeeErrors.NameTaken);
            var restored = fee.Restore();
            if (restored.IsFailure)
                return restored;
        }

        await db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}

// ============================================================ Bảng giá

public sealed record AddFeePriceCommand(Guid FeeTypeId, FeePriceInput Price) : IRequest<Result<CreatedWithWarnings>>;

public sealed class AddFeePriceCommandValidator : AbstractValidator<AddFeePriceCommand>
{
    // Giới hạn giá theo nhóm kiểm lại ở handler (validator chưa biết nhóm) ⇒ ở đây dùng mức rộng của nhóm Fixed.
    public AddFeePriceCommandValidator(TimeProvider clock) => this.PriceRules(x => x.Price, _ => null, "", clock);
}

public sealed class AddFeePriceHandler(IAppDbContext db, IFeePriceLockReader locks, IFeeSettings settings)
    : IRequestHandler<AddFeePriceCommand, Result<CreatedWithWarnings>>
{
    public async ValueTask<Result<CreatedWithWarnings>> Handle(AddFeePriceCommand request, CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await db.LockForUpdateAsync<FeeType>(request.FeeTypeId, cancellationToken);

        var fee = await db.FeeTypes.Include(f => f.Prices).FirstOrDefaultAsync(f => f.Id == request.FeeTypeId, cancellationToken);
        if (fee is null)
            return FeeErrors.NotFound;
        var price = request.Price;
        if (price.UnitPrice is { } unit && !FeeRules.IsValidPrice(unit, fee.Group))
            return Error.Validation("INVALID_AMOUNT", $"Đơn giá tối đa {FeeRules.MaxPrice(fee.Group):N0}đ, tối đa 2 số lẻ.");

        var lockedUntil = await locks.GetLockedUntilAsync(fee.Id, cancellationToken);
        var added = fee.AddPrice(price.EffectiveFrom, price.UnitPrice ?? 0, price.Note, lockedUntil, price.Tiers);
        if (added.IsFailure)
            return added.Error!;

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new CreatedWithWarnings(added.Value!.Id, FeeWarnings.ForPrice(fee, added.Value!, settings));
    }
}

public sealed record DeleteFeePriceCommand(Guid FeeTypeId, Guid PriceId) : IRequest<Result>;

public sealed class DeleteFeePriceHandler(IAppDbContext db, IFeePriceLockReader locks) : IRequestHandler<DeleteFeePriceCommand, Result>
{
    public async ValueTask<Result> Handle(DeleteFeePriceCommand request, CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await db.LockForUpdateAsync<FeeType>(request.FeeTypeId, cancellationToken);

        var fee = await db.FeeTypes.Include(f => f.Prices).FirstOrDefaultAsync(f => f.Id == request.FeeTypeId, cancellationToken);
        if (fee is null)
            return Result.Failure(FeeErrors.NotFound);

        var removed = fee.RemovePrice(request.PriceId, await locks.GetLockedUntilAsync(fee.Id, cancellationToken));
        if (removed.IsFailure)
            return removed;

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return Result.Success();
    }
}

// ============================================================ Sao chép danh mục (FE-UC-06)

public sealed record CopyFeeCatalogCommand(Guid SourcePropertyId, Guid TargetPropertyId, bool IncludePrices, DateOnly? EffectiveFrom)
    : IRequest<Result<CopyFeeCatalogResult>>;

public sealed record CopyFeeCatalogResult(int Copied, IReadOnlyList<string> Skipped);

/// <summary>
/// Chép khoản thu chưa ngừng dùng từ khu nguồn sang khu đích; trùng tên / trùng mã hệ thống (điện, nước) thì bỏ qua.
/// Kèm giá: chép giá đang hiệu lực, hiệu lực từ <c>effectiveFrom</c> (mặc định hôm nay).
/// </summary>
public sealed class CopyFeeCatalogHandler(IAppDbContext db, TimeProvider clock) : IRequestHandler<CopyFeeCatalogCommand, Result<CopyFeeCatalogResult>>
{
    public async ValueTask<Result<CopyFeeCatalogResult>> Handle(CopyFeeCatalogCommand request, CancellationToken cancellationToken)
    {
        var ids = new[] { request.SourcePropertyId, request.TargetPropertyId };
        var properties = await db.Properties.AsNoTracking().Where(p => ids.Contains(p.Id)).ToListAsync(cancellationToken);
        if (properties.Count != ids.Distinct().Count() || request.SourcePropertyId == request.TargetPropertyId)
            return PropertyErrors.PropertyNotFound;
        if (properties.Single(p => p.Id == request.TargetPropertyId).IsArchived)
            return PropertyErrors.PropertyArchived;

        var source = await db.FeeTypes.AsNoTracking().Include(f => f.Prices)
            .Where(f => f.PropertyId == request.SourcePropertyId && f.ArchivedAt == null).ToListAsync(cancellationToken);
        var target = await db.FeeTypes.Include(f => f.Prices)
            .Where(f => f.PropertyId == request.TargetPropertyId && f.ArchivedAt == null).ToListAsync(cancellationToken);

        var from = request.EffectiveFrom ?? clock.GetUtcNow().ToBusinessDate();
        var skipped = new List<string>();
        var copied = 0;
        foreach (var fee in source.OrderBy(f => f.SortOrder))
        {
            var existing = target.FirstOrDefault(t => t.NameNormalized == fee.NameNormalized
                || (fee.SystemCode != null && t.SystemCode == fee.SystemCode));
            var price = request.IncludePrices ? fee.ResolvePrice(from) : null;

            // Điện / Nước mặc định của khu đích chưa có giá ⇒ chép giá vào khoản sẵn có thay vì bỏ qua.
            if (existing is not null)
            {
                if (price is not null && existing.SystemCode is not null && existing.SystemCode == fee.SystemCode && existing.Prices.Count == 0)
                {
                    existing.AddPrice(from, price.UnitPrice, price.Note, null, price.Tiers);
                    copied++;
                }
                else
                    skipped.Add(fee.Name);
                continue;
            }

            var clone = FeeType.Create(request.TargetPropertyId, fee.Name, fee.Group, fee.ChargeBasis, fee.Unit, fee.AutoAttach,
                fee.DefaultQuantity, fee.SortOrder, fee.SystemCode, fee.VehicleType);
            if (price is not null)
                clone.AddPrice(from, price.UnitPrice, price.Note, null, price.Tiers);
            db.FeeTypes.Add(clone);
            copied++;
        }

        await db.SaveChangesAsync(cancellationToken);
        return new CopyFeeCatalogResult(copied, skipped);
    }
}
