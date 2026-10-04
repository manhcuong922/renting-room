using renting_room.Domain.Common;
using renting_room.Domain.Contracts;

namespace renting_room.Domain.Fees;

/// <summary>Nhóm khoản thu (FE-BR-02: bất biến sau khi tạo). Phụ thu không thuộc danh mục — nhập trên phiếu (M07).</summary>
public enum FeeGroup
{
    Metered,  // Điện nước: theo công tơ của phòng — (mới − cũ) × một đơn giá
    Service   // Dịch vụ: gắn HĐ, tính theo ChargeBasis
}

/// <summary>Cách tính của nhóm <see cref="FeeGroup.Service"/>.</summary>
public enum ChargeBasis
{
    PerRoom,      // × 1 phòng (mạng, rác)
    PerOccupant,  // × số người ở (nước theo đầu người)
    PerUnit       // × số gói đăng ký (giữ xe: 2 xe = 2 gói)
}

public static class FeeSystemCodes
{
    public const string Electricity = "ELECTRICITY";
    public const string Water = "WATER";
}

/// <summary>
/// Khoản thu của một khu trọ (M04): điện, nước, rác, wifi, giữ xe… Cách tính chọn theo nhóm + cơ sở tính.
/// Giá đi theo thời gian (<see cref="FeePrice"/>), một giá cho mỗi đơn vị (không bậc thang — FE-BR-15).
/// Khoản <see cref="FeeGroup.Metered"/> đi theo <b>công tơ của phòng</b> (M06), không gắn vào hợp đồng (FE-BR-17);
/// khoản <see cref="FeeGroup.Service"/> gắn vào hợp đồng qua <c>contract_fees</c> (M05) và có thể đặt giá riêng.
/// </summary>
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

    /// <summary>CT-BR-22: dịch vụ theo số gói dùng làm phí giữ loại xe này (đối chiếu số xe đăng ký trên HĐ). Chỉ <see cref="Fees.ChargeBasis.PerUnit"/>.</summary>
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

        var fee = new FeeType { Id = Guid.NewGuid(), PropertyId = propertyId, Group = group, ChargeBasis = chargeBasis, SystemCode = systemCode };
        fee.Update(name, unit, autoAttach, defaultQuantity, sortOrder, vehicleType);
        return fee;
    }

    /// <summary>Điện + Nước theo công tơ, chưa có giá (FE-UC-01). Đi theo công tơ của phòng nên không gắn vào HĐ.</summary>
    public static IReadOnlyList<FeeType> DefaultsFor(Guid propertyId) =>
    [
        Create(propertyId, "Điện", FeeGroup.Metered, null, "kWh", autoAttach: false, null, 0, FeeSystemCodes.Electricity),
        Create(propertyId, "Nước", FeeGroup.Metered, null, "m³", autoAttach: false, null, 1, FeeSystemCodes.Water)
    ];

    /// <summary>Không đổi nhóm / cơ sở tính (FE-BR-02) — cần đổi thì tạo khoản mới, ngừng dùng khoản cũ.</summary>
    public void Update(string name, string unit, bool autoAttach, decimal? defaultQuantity, int sortOrder, VehicleType? vehicleType = null)
    {
        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(unit))
            throw new ArgumentException("Name and unit are required.");
        if (!IsPerUnit && (defaultQuantity is not null || vehicleType is not null))
            throw new ArgumentException("Default quantity and vehicle type are for PerUnit services only.");

        VehicleType = vehicleType;
        Name = name.Trim();
        NameNormalized = NormalizeName(name);
        Unit = unit.Trim();
        AutoAttach = autoAttach && Group != FeeGroup.Metered; // FE-BR-17: khoản theo công tơ không gắn vào HĐ
        DefaultQuantity = defaultQuantity;
        SortOrder = sortOrder;
    }

    public static string NormalizeName(string name) => name.Trim().ToLowerInvariant();

    /// <summary>Số gói mặc định khi gắn vào HĐ: <c>PerUnit</c> dùng <see cref="DefaultQuantity"/> (mặc định 1), cách khác luôn 1.</summary>
    public decimal AttachQuantity => IsPerUnit ? DefaultQuantity ?? 1 : 1;

    /// <param name="lockedUntil">Ngày cuối của dòng phiếu đã chốt dùng khoản này (M07) — không thêm giá hồi tố trước ngày đó (FE-BR-07).</param>
    public Result<FeePrice> AddPrice(DateOnly effectiveFrom, decimal unitPrice, string? note, DateOnly? lockedUntil)
    {
        if (IsArchived)
            return Result.Failure<FeePrice>(FeeErrors.Archived);
        if (_prices.Any(p => p.EffectiveFrom == effectiveFrom))
            return Result.Failure<FeePrice>(FeeErrors.PriceDateExists);
        if (lockedUntil is { } locked && effectiveFrom <= locked)
            return Result.Failure<FeePrice>(FeeErrors.PriceLocked);

        var price = new FeePrice(Id, effectiveFrom, unitPrice, note);
        _prices.Add(price);
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
        return Result.Success();
    }

    /// <summary>FE-BR-10: bản giá có ngày hiệu lực lớn nhất ≤ <paramref name="date"/>; null nếu chưa có giá.</summary>
    public FeePrice? ResolvePrice(DateOnly date) =>
        _prices.Where(p => p.EffectiveFrom <= date).MaxBy(p => p.EffectiveFrom);

    public Result Archive(DateTimeOffset now)
    {
        if (IsArchived)
            return Result.Failure(FeeErrors.AlreadyArchived);
        ArchivedAt = now;
        return Result.Success();
    }

    public Result Restore()
    {
        if (!IsArchived)
            return Result.Failure(FeeErrors.NotArchived);
        ArchivedAt = null;
        return Result.Success();
    }
}

/// <summary>Đơn giá có hiệu lực từ <see cref="EffectiveFrom"/> tới trước bản kế tiếp. Không sửa — xóa rồi thêm (FE-BR-07/08).</summary>
public sealed class FeePrice : TenantEntity
{
    private FeePrice() { } // EF Core

    internal FeePrice(Guid feeTypeId, DateOnly effectiveFrom, decimal unitPrice, string? note)
    {
        Id = Guid.NewGuid();
        FeeTypeId = feeTypeId;
        EffectiveFrom = effectiveFrom;
        UnitPrice = unitPrice;
        Note = TextNormalizer.TrimToNull(note);
    }

    public Guid FeeTypeId { get; private set; }
    public DateOnly EffectiveFrom { get; private set; }
    public decimal UnitPrice { get; private set; }
    public string? Note { get; private set; }

    /// <summary>
    /// Thành tiền = số lượng × đơn giá, làm tròn tới đồng. Một giá cho mọi mức sử dụng — không bậc thang như hộ gia đình (FE-BR-15).
    /// <paramref name="unitPriceOverride"/> = giá riêng của HĐ (khoản cố định / theo số lượng).
    /// </summary>
    public decimal Amount(decimal quantity, decimal? unitPriceOverride = null)
    {
        if (quantity < 0)
            throw new ArgumentOutOfRangeException(nameof(quantity), "Quantity cannot be negative.");
        return Round(quantity * (unitPriceOverride ?? UnitPrice));
    }

    private static decimal Round(decimal value) => Math.Round(value, 0, MidpointRounding.AwayFromZero);
}

public static class FeeErrors
{
    public static readonly Error NotFound = Error.NotFound("FEE_TYPE_NOT_FOUND", "Không tìm thấy khoản thu.");
    public static readonly Error NameTaken = Error.Conflict("FEE_NAME_TAKEN", "Tên khoản thu đã có trong khu.");
    public static readonly Error SystemCodeTaken = Error.Conflict("FEE_SYSTEM_CODE_TAKEN", "Khu đã có khoản thu điện / nước hệ thống đang dùng.");
    public static readonly Error Archived = Error.BusinessRule("FEE_ARCHIVED", "Khoản thu đã ngừng dùng.");
    public static readonly Error AlreadyArchived = Error.Conflict("FEE_ALREADY_ARCHIVED", "Khoản thu đã ngừng dùng.");
    public static readonly Error NotArchived = Error.Conflict("FEE_NOT_ARCHIVED", "Khoản thu đang được dùng.");
    public static readonly Error HasActiveMeters = Error.BusinessRule("FEE_HAS_ACTIVE_METERS",
        "Còn công tơ đang hoạt động cho khoản này — tháo / thay công tơ trước khi ngừng dùng.");
    public static readonly Error InUse = Error.BusinessRule("FEE_IN_USE",
        "Khoản thu đang gắn với hợp đồng còn hiệu lực — gỡ khỏi các hợp đồng trước khi ngừng dùng.");
    public static readonly Error PriceNotFound = Error.NotFound("FEE_PRICE_NOT_FOUND", "Không tìm thấy bản giá.");
    public static readonly Error PriceDateExists = Error.Conflict("FEE_PRICE_DATE_EXISTS", "Đã có bản giá hiệu lực từ ngày này.");
    public static readonly Error PriceLocked = Error.BusinessRule("FEE_PRICE_LOCKED",
        "Ngày hiệu lực thuộc kỳ đã chốt phiếu — chỉ đổi giá từ kỳ chưa chốt.");
    public static readonly Error MeteredFollowsRoom = Error.Validation("FEE_METERED_FOLLOWS_ROOM",
        "Điện / nước theo công tơ tính theo công tơ của phòng — không gắn vào hợp đồng.");
    public static readonly Error NotInProperty = Error.BusinessRule("FEE_NOT_IN_PROPERTY", "Khoản thu không thuộc khu của hợp đồng.");
}
