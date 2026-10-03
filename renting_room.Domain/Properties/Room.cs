using renting_room.Domain.Common;

namespace renting_room.Domain.Properties;

/// <summary>Trạng thái hiển thị — DẪN XUẤT từ hợp đồng, không lưu (PR-BR-02).</summary>
public enum RoomDisplayStatus
{
    Vacant,
    Reserved,
    Occupied,
    Maintenance,
    Archived
}

/// <summary>Phòng — đơn vị cho thuê nhỏ nhất, thuộc 1 khu. Không lưu "đang thuê": suy ra từ hợp đồng.</summary>
public sealed class Room : TenantEntity
{
    public const int MaxOccupantsLimit = 20;

    private Room() { } // EF Core

    public Guid PropertyId { get; private set; }
    public string Code { get; private set; } = null!;
    public string? Floor { get; private set; }
    public decimal? AreaM2 { get; private set; }
    public int MaxOccupants { get; private set; }
    public decimal? ListedRent { get; private set; }
    public decimal? DefaultDeposit { get; private set; }
    public string[] Amenities { get; private set; } = [];
    public string? Description { get; private set; }
    public bool IsUnderMaintenance { get; private set; }
    public string? MaintenanceNote { get; private set; }
    public DateTimeOffset? ArchivedAt { get; private set; }

    public bool IsArchived => ArchivedAt is not null;

    public static Room Create(Guid propertyId, string code, RoomSpec spec)
    {
        if (propertyId == Guid.Empty)
            throw new ArgumentException("Property id is required.", nameof(propertyId));

        var room = new Room { Id = Guid.NewGuid(), PropertyId = propertyId };
        room.Update(code, spec);
        return room;
    }

    public void Update(string code, RoomSpec spec)
    {
        if (string.IsNullOrWhiteSpace(code))
            throw new ArgumentException("Room code is required.", nameof(code));
        if (spec.MaxOccupants is < 1 or > MaxOccupantsLimit)
            throw new ArgumentOutOfRangeException(nameof(spec), "MaxOccupants must be between 1 and 20.");

        Code = TextNormalizer.NormalizeCode(code);
        Floor = TextNormalizer.TrimToNull(spec.Floor);
        AreaM2 = spec.AreaM2;
        MaxOccupants = spec.MaxOccupants;
        ListedRent = spec.ListedRent;
        DefaultDeposit = spec.DefaultDeposit;
        Amenities = spec.Amenities.Select(a => a.Trim().ToLowerInvariant()).Distinct().ToArray();
        Description = TextNormalizer.TrimToNull(spec.Description);
    }

    /// <summary>Điều kiện "không có hợp đồng hiệu lực" do Application kiểm tra dưới khóa hàng (PR-BR-03).</summary>
    public Result StartMaintenance(string? note)
    {
        if (IsArchived)
            return Result.Failure(PropertyErrors.RoomArchived);
        if (IsUnderMaintenance)
            return Result.Failure(PropertyErrors.RoomAlreadyUnderMaintenance);

        IsUnderMaintenance = true;
        MaintenanceNote = TextNormalizer.TrimToNull(note);
        return Result.Success();
    }

    public Result EndMaintenance()
    {
        if (!IsUnderMaintenance)
            return Result.Failure(PropertyErrors.RoomNotUnderMaintenance);

        IsUnderMaintenance = false;
        MaintenanceNote = null;
        return Result.Success();
    }

    public Result Archive(DateTimeOffset now)
    {
        if (IsArchived)
            return Result.Failure(PropertyErrors.RoomArchived);

        ArchivedAt = now;
        IsUnderMaintenance = false;
        MaintenanceNote = null;
        return Result.Success();
    }

    public Result Restore()
    {
        if (!IsArchived)
            return Result.Failure(PropertyErrors.RoomNotArchived);

        ArchivedAt = null;
        return Result.Success();
    }
}

public sealed record RoomSpec(
    string? Floor,
    decimal? AreaM2,
    int MaxOccupants,
    decimal? ListedRent,
    decimal? DefaultDeposit,
    IReadOnlyCollection<string> Amenities,
    string? Description);
