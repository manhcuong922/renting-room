using renting_room.Domain.Common;

namespace renting_room.Domain.Contracts;

/// <summary>Giá thuê hiệu lực từ đầu một kỳ (bản đầu = giá lúc ký, các bản sau = phụ lục) — CT-BR-05.</summary>
public sealed class ContractRentTerm : TenantEntity
{
    private ContractRentTerm() { } // EF Core

    internal ContractRentTerm(Guid contractId, DateOnly effectiveFrom, decimal monthlyRent, string? addendumNo, string? note)
    {
        if (monthlyRent <= 0)
            throw new ArgumentOutOfRangeException(nameof(monthlyRent));

        Id = Guid.CreateVersion7();
        ContractId = contractId;
        EffectiveFrom = effectiveFrom;
        MonthlyRent = monthlyRent;
        AddendumNo = TextNormalizer.TrimToNull(addendumNo);
        Note = TextNormalizer.TrimToNull(note);
    }

    public Guid ContractId { get; private set; }
    public DateOnly EffectiveFrom { get; private set; }
    public decimal MonthlyRent { get; private set; }
    public string? AddendumNo { get; private set; }
    public string? Note { get; private set; }

    internal void Replace(decimal monthlyRent, string? addendumNo, string? note)
    {
        if (monthlyRent <= 0)
            throw new ArgumentOutOfRangeException(nameof(monthlyRent));
        MonthlyRent = monthlyRent;
        AddendumNo = TextNormalizer.TrimToNull(addendumNo);
        Note = TextNormalizer.TrimToNull(note);
    }
}

/// <summary>
/// CT-BR-14: lịch sử chuyển phòng — HĐ đã ở phòng <see cref="RoomId"/> trong [<see cref="FromDate"/>, <see cref="ToDate"/>] (ngày chuyển đi tính là
/// còn ở). Phòng hiện tại nằm trên HĐ (<c>room_id</c>, <c>room_since</c>). Bất biến sau khi ghi.
/// </summary>
public sealed class ContractRoomMove : TenantEntity
{
    private ContractRoomMove() { } // EF Core

    internal ContractRoomMove(Guid contractId, Guid roomId, DateOnly fromDate, DateOnly toDate)
    {
        if (toDate < fromDate)
            throw new ArgumentOutOfRangeException(nameof(toDate));
        Id = Guid.CreateVersion7();
        ContractId = contractId;
        RoomId = roomId;
        FromDate = fromDate;
        ToDate = toDate;
    }

    public Guid ContractId { get; private set; }
    public Guid RoomId { get; private set; }
    public DateOnly FromDate { get; private set; }
    public DateOnly ToDate { get; private set; }
}

/// <summary>Một khoảng HĐ ở 1 phòng (phòng cũ đã chuyển đi hoặc phòng hiện tại — <paramref name="To"/> null = chưa trả phòng).</summary>
public sealed record RoomStay(Guid RoomId, DateOnly From, DateOnly? To)
{
    public bool Overlaps(DateOnly from, DateOnly to) => From <= to && (To is null || To >= from);
}

/// <summary>Người ở thực tế (có thể khác người đại diện ký) — ngày vào/ra riêng (CT-BR-08).</summary>
public sealed class ContractOccupant : TenantEntity
{
    private ContractOccupant() { } // EF Core

    internal ContractOccupant(Guid contractId, OccupantInput input)
    {
        Id = Guid.CreateVersion7();
        ContractId = contractId;
        RenterId = input.RenterId;
        MoveInDate = input.MoveInDate;
        ExpectedEndDate = input.ExpectedEndDate;
        Relationship = TextNormalizer.TrimToNull(input.Relationship);
        RelationshipType = input.RelationshipType;
        GuardianConsent = input.GuardianConsent;
        Note = TextNormalizer.TrimToNull(input.Note);
    }

    public Guid ContractId { get; private set; }
    public Guid RenterId { get; private set; }
    public DateOnly MoveInDate { get; private set; }
    public DateOnly? MoveOutDate { get; private set; }
    public DateOnly? ExpectedEndDate { get; private set; }
    public string? Relationship { get; private set; }

    /// <summary>CT-BR-28: quan hệ với người đứng tên HĐ (null với chính người đứng tên và dữ liệu cũ).</summary>
    public OccupantRelationship? RelationshipType { get; private set; }

    /// <summary>CT-BR-30: người chưa thành niên đã có đồng ý của cha, mẹ hoặc người giám hộ.</summary>
    public bool GuardianConsent { get; private set; }
    public string? Note { get; private set; }

    /// <summary>Dạng <see cref="OccupantInput"/> để kiểm tra quan hệ cùng người mới (ngày ra thực tế thay cho dự kiến).</summary>
    public OccupantInput ToInput() =>
        new(RenterId, MoveInDate, MoveOutDate ?? ExpectedEndDate, Relationship, Note, RelationshipType, GuardianConsent);

    public bool IsStayingOn(DateOnly date) => MoveInDate <= date && (MoveOutDate is null || MoveOutDate >= date);

    /// <summary>Khoảng ở giao với [from, ∞).</summary>
    public bool OverlapsFrom(DateOnly from) => MoveOutDate is null || MoveOutDate >= from;

    internal void MoveOut(DateOnly date) => MoveOutDate = date;
}

/// <summary>Tài sản bàn giao kèm phòng (CT-UC-13) — căn cứ trừ cọc khi trả phòng.</summary>
public sealed class ContractAsset : TenantEntity
{
    private ContractAsset() { } // EF Core

    internal ContractAsset(Guid contractId, AssetInput input)
    {
        Id = Guid.CreateVersion7();
        ContractId = contractId;
        Update(input);
    }

    public Guid ContractId { get; private set; }
    public string Name { get; private set; } = null!;
    public int Quantity { get; private set; }
    public string? ConditionAtHandover { get; private set; }
    public string? ConditionAtReturn { get; private set; }
    public decimal? ValueEstimate { get; private set; }
    public string? Note { get; private set; }

    internal void Update(AssetInput input)
    {
        if (string.IsNullOrWhiteSpace(input.Name))
            throw new ArgumentException("Asset name is required.", nameof(input));
        if (input.Quantity is < 1 or > 100)
            throw new ArgumentOutOfRangeException(nameof(input), "Quantity must be between 1 and 100.");

        Name = input.Name.Trim();
        Quantity = input.Quantity;
        ConditionAtHandover = TextNormalizer.TrimToNull(input.ConditionAtHandover);
        ValueEstimate = input.ValueEstimate;
        Note = TextNormalizer.TrimToNull(input.Note);
    }

    /// <summary>CT-BR-23 (đổi 10/10/2026): chỉ ghi tình trạng lúc trả — tiền bồi thường thu bằng phụ thu trên phiếu (BL-BR-23).</summary>
    internal void RecordReturn(string? condition) => ConditionAtReturn = TextNormalizer.TrimToNull(condition);
}

public sealed record AssetInput(string Name, int Quantity, string? ConditionAtHandover, decimal? ValueEstimate, string? Note);

/// <summary>Xe đăng ký gửi (CT-UC-14).</summary>
public sealed class ContractVehicle : TenantEntity
{
    private ContractVehicle() { } // EF Core

    internal ContractVehicle(Guid contractId, VehicleInput input)
    {
        Id = Guid.CreateVersion7();
        ContractId = contractId;
        RenterId = input.RenterId;
        VehicleType = input.VehicleType;
        PlateNumber = NormalizePlate(input.PlateNumber);
        BrandColor = TextNormalizer.TrimToNull(input.BrandColor);
        RegisteredFrom = input.RegisteredFrom;
        Note = TextNormalizer.TrimToNull(input.Note);
    }

    public Guid ContractId { get; private set; }
    public Guid? RenterId { get; private set; }
    public VehicleType VehicleType { get; private set; }
    public string? PlateNumber { get; private set; }
    public string? BrandColor { get; private set; }
    public DateOnly RegisteredFrom { get; private set; }
    public DateOnly? RegisteredTo { get; private set; }
    public string? Note { get; private set; }

    public bool IsActive => RegisteredTo is null;

    internal void MoveStart(DateOnly date) => RegisteredFrom = date;

    /// <summary>"29-b1 123.45" → "29B112345": một dạng duy nhất để unique index chặn đăng ký trùng.</summary>
    public static string? NormalizePlate(string? plate) =>
        string.IsNullOrWhiteSpace(plate) ? null : new string(plate.Where(char.IsLetterOrDigit).ToArray()).ToUpperInvariant();

    internal void End(DateOnly date)
    {
        if (date < RegisteredFrom)
            throw new ArgumentOutOfRangeException(nameof(date));

        RegisteredTo = date;
    }

    /// <summary>RT-BR-06: ẩn danh — biển số, hiệu – màu nhận diện được người; chỉ xe đã kết thúc.</summary>
    internal bool EraseIdentity()
    {
        if (IsActive || (PlateNumber is null && BrandColor is null))
            return false;
        PlateNumber = null;
        BrandColor = null;
        return true;
    }
}

public sealed record VehicleInput(Guid? RenterId, VehicleType VehicleType, string? PlateNumber, string? BrandColor, DateOnly RegisteredFrom, string? Note);

/// <summary>
/// Đăng ký khoản thu của HĐ trong một khoảng hiệu lực [EffectiveFrom, EffectiveTo] (CT-BR-06). Đổi số lượng / giá riêng
/// từ một kỳ = đóng bản cũ ngày trước đó + thêm bản mới ⇒ giữ lịch sử để tính lại các kỳ cũ.
/// </summary>
public sealed class ContractFee : TenantEntity
{
    private ContractFee() { } // EF Core

    internal ContractFee(Guid contractId, Guid propertyId, ContractFeeInput input, DateOnly effectiveFrom)
    {
        Id = Guid.CreateVersion7();
        ContractId = contractId;
        PropertyId = propertyId;
        FeeTypeId = input.FeeTypeId;
        Quantity = input.Quantity;
        UnitPriceOverride = input.UnitPriceOverride;
        EffectiveFrom = effectiveFrom;
    }

    public Guid ContractId { get; private set; }
    public Guid PropertyId { get; private set; }
    public Guid FeeTypeId { get; private set; }
    public decimal Quantity { get; private set; }
    public decimal? UnitPriceOverride { get; private set; }
    public DateOnly EffectiveFrom { get; private set; }
    public DateOnly? EffectiveTo { get; private set; }

    public bool Covers(DateOnly date) => EffectiveFrom <= date && (EffectiveTo is null || EffectiveTo >= date);

    internal void Replace(ContractFeeInput input)
    {
        Quantity = input.Quantity;
        UnitPriceOverride = input.UnitPriceOverride;
    }

    internal void EndOn(DateOnly date) => EffectiveTo = date;
}
