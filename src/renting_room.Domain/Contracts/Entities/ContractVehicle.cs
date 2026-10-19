using renting_room.Domain.Common;

namespace renting_room.Domain.Contracts;

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
