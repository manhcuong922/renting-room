using renting_room.Domain.Common;
using renting_room.Domain.Contracts;

namespace renting_room.Domain.Fees;

public sealed class FeeType : TenantEntity
{
    public const decimal MaxQuantity = 100;

    private readonly List<FeePrice> _prices = [];

    private FeeType() { } // EF Core

    public Guid PropertyId { get; private set; }
    public string Name { get; private set; } = null!;
    public string NameNormalized { get; private set; } = null!;
    public FeeGroup Group { get; private set; }
    public ChargeBasis? ChargeBasis { get; private set; }
    public string Unit { get; private set; } = null!;
    public string? SystemCode { get; private set; }
    public bool AutoAttach { get; private set; }
    public decimal? DefaultQuantity { get; private set; }
    public int SortOrder { get; private set; }

    public VehicleType? VehicleType { get; private set; }
    public DateTimeOffset? ArchivedAt { get; private set; }

    public IReadOnlyList<FeePrice> Prices => _prices;
    public bool IsArchived => ArchivedAt is not null;
    public bool IsPerUnit => ChargeBasis == Fees.ChargeBasis.PerUnit;

    public static FeeType Create(
        Guid propertyId, string name, FeeGroup group, ChargeBasis? chargeBasis, string unit, bool autoAttach,
        decimal? defaultQuantity, int sortOrder, string? systemCode = null, VehicleType? vehicleType = null)
    {
        if ((group == FeeGroup.Service) != (chargeBasis is not null))
            throw new ArgumentException("Charge basis is required for Service group only (FE-BR-03).");
        if (systemCode is not null && group != FeeGroup.Metered)
            throw new ArgumentException("System code is for metered fees only.");

        var fee = new FeeType { Id = Guid.CreateVersion7(), PropertyId = propertyId, Group = group, ChargeBasis = chargeBasis, SystemCode = systemCode };
        fee.Update(name, unit, autoAttach, defaultQuantity, sortOrder, vehicleType);
        fee.Raise(new FeeTypeCreated(fee.Id));
        return fee;
    }

    public static IReadOnlyList<FeeType> DefaultsFor(Guid propertyId) =>
    [
        Create(propertyId, "Điện", FeeGroup.Metered, null, "kWh", autoAttach: false, null, 0, FeeSystemCodes.Electricity),
        Create(propertyId, "Nước", FeeGroup.Metered, null, "m³", autoAttach: false, null, 1, FeeSystemCodes.Water)
    ];

    public void Update(string name, string unit, bool autoAttach, decimal? defaultQuantity, int sortOrder, VehicleType? vehicleType = null)
    {
        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(unit))
            throw new ArgumentException("Name and unit are required.");
        if (!IsPerUnit && (defaultQuantity is not null || vehicleType is not null))
            throw new ArgumentException("Default quantity and vehicle type are for PerUnit services only.");

        var before = (VehicleType, Name, NameNormalized, Unit, AutoAttach, DefaultQuantity, SortOrder);
        VehicleType = vehicleType;
        Name = name.Trim();
        NameNormalized = NormalizeName(name);
        Unit = unit.Trim();
        AutoAttach = autoAttach && Group != FeeGroup.Metered;
        DefaultQuantity = defaultQuantity;
        SortOrder = sortOrder;
        if (before != (VehicleType, Name, NameNormalized, Unit, AutoAttach, DefaultQuantity, SortOrder))
            Raise(new FeeTypeChanged(Id));
    }

    public static string NormalizeName(string name) => name.Trim().ToLowerInvariant();

    /// <summary>Số gói mặc định khi gắn vào HĐ: <c>PerUnit</c> dùng <see cref="DefaultQuantity"/> (mặc định 1), cách khác luôn 1.</summary>
    public decimal AttachQuantity => IsPerUnit ? DefaultQuantity ?? 1 : 1;

    public Result<FeePrice> AddPrice(DateOnly effectiveFrom, decimal unitPrice, string? note, DateOnly? lockedUntil, IReadOnlyList<PriceTier>? tiers = null)
    {
        if (IsArchived)
            return Result.Failure<FeePrice>(FeeErrors.Archived);
        if (tiers is { Count: > 0 } && Group != FeeGroup.Metered)
            return Result.Failure<FeePrice>(FeeErrors.TiersForMeteredOnly);
        if (_prices.Any(p => p.EffectiveFrom == effectiveFrom))
            return Result.Failure<FeePrice>(FeeErrors.PriceDateExists);
        if (lockedUntil is { } locked && effectiveFrom <= locked)
            return Result.Failure<FeePrice>(FeeErrors.PriceLocked);

        var price = new FeePrice(Id, effectiveFrom, unitPrice, note, tiers);
        _prices.Add(price);
        Raise(new FeePriceChanged(Id, effectiveFrom));
        return Result.Success(price);
    }

    public Result RemovePrice(Guid priceId, DateOnly? lockedUntil)
    {
        var price = _prices.FirstOrDefault(p => p.Id == priceId);
        if (price is null)
            return Result.Failure(FeeErrors.PriceNotFound);
        if (lockedUntil is { } locked && price.EffectiveFrom <= locked)
            return Result.Failure(FeeErrors.PriceLocked);

        _prices.Remove(price);
        Raise(new FeePriceChanged(Id, price.EffectiveFrom));
        return Result.Success();
    }

    public FeePrice? ResolvePrice(DateOnly date) =>
        _prices.Where(p => p.EffectiveFrom <= date).MaxBy(p => p.EffectiveFrom);

    public Result Archive(DateTimeOffset now)
    {
        if (IsArchived)
            return Result.Failure(FeeErrors.AlreadyArchived);
        ArchivedAt = now;
        Raise(new FeeTypeChanged(Id));
        return Result.Success();
    }

    public Result Restore()
    {
        if (!IsArchived)
            return Result.Failure(FeeErrors.NotArchived);
        ArchivedAt = null;
        Raise(new FeeTypeChanged(Id));
        return Result.Success();
    }
}
