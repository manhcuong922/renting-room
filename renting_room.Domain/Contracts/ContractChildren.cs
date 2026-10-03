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

        Id = Guid.NewGuid();
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
}

/// <summary>Người ở thực tế (có thể khác người đại diện ký) — ngày vào/ra riêng (CT-BR-08).</summary>
public sealed class ContractOccupant : TenantEntity
{
    private ContractOccupant() { } // EF Core

    internal ContractOccupant(Guid contractId, OccupantInput input)
    {
        Id = Guid.NewGuid();
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
        Id = Guid.NewGuid();
        ContractId = contractId;
        Update(input);
    }

    public Guid ContractId { get; private set; }
    public string Name { get; private set; } = null!;
    public int Quantity { get; private set; }
    public string? ConditionAtHandover { get; private set; }
    public string? ConditionAtReturn { get; private set; }
    public decimal? ValueEstimate { get; private set; }
    public decimal? CompensationValue { get; private set; }
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

    internal void RecordReturn(string? condition, decimal? compensationValue)
    {
        if (compensationValue is < 0)
            throw new ArgumentOutOfRangeException(nameof(compensationValue));

        ConditionAtReturn = TextNormalizer.TrimToNull(condition);
        CompensationValue = compensationValue;
    }
}

public sealed record AssetInput(string Name, int Quantity, string? ConditionAtHandover, decimal? ValueEstimate, string? Note);

/// <summary>Xe đăng ký gửi (CT-UC-14).</summary>
public sealed class ContractVehicle : TenantEntity
{
    private ContractVehicle() { } // EF Core

    internal ContractVehicle(Guid contractId, VehicleInput input)
    {
        Id = Guid.NewGuid();
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
}

public sealed record VehicleInput(Guid? RenterId, VehicleType VehicleType, string? PlateNumber, string? BrandColor, DateOnly RegisteredFrom, string? Note);
