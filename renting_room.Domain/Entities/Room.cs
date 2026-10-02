using renting_room.Domain.Common;
using renting_room.Domain.Enums;

namespace renting_room.Domain.Entities;

/// <summary>
/// Phòng — bản scaffold tạm. Sẽ được viết lại theo plan M02 (thuộc khu trọ, trạng thái dẫn xuất từ hợp đồng).
/// Hiện đã thuộc về tổ chức để không lộ dữ liệu giữa các chủ trọ.
/// </summary>
public class Room : TenantEntity
{
    private Room() { } // EF Core

    public string Name { get; private set; } = null!;
    public decimal MonthlyRent { get; private set; }
    public RoomStatus Status { get; private set; }

    public static Room Create(string name, decimal monthlyRent)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Room name is required.", nameof(name));

        if (monthlyRent <= 0)
            throw new ArgumentException("Monthly rent must be greater than zero.", nameof(monthlyRent));

        return new Room
        {
            Id = Guid.NewGuid(),
            Name = name.Trim(),
            MonthlyRent = monthlyRent,
            Status = RoomStatus.Available
        };
    }

    public Result MarkOccupied()
    {
        if (Status is not RoomStatus.Available)
            return Result.Failure(RoomErrors.NotAvailable);

        Status = RoomStatus.Occupied;
        return Result.Success();
    }

    public Result MarkAvailable()
    {
        if (Status is RoomStatus.Available)
            return Result.Failure(RoomErrors.AlreadyAvailable);

        Status = RoomStatus.Available;
        return Result.Success();
    }
}

public static class RoomErrors
{
    public static readonly Error NotFound =
        Error.NotFound("ROOM_NOT_FOUND", "Không tìm thấy phòng.");

    public static readonly Error NotAvailable =
        Error.BusinessRule("ROOM_NOT_AVAILABLE", "Chỉ phòng trống mới được chuyển sang đang thuê.");

    public static readonly Error AlreadyAvailable =
        Error.BusinessRule("ROOM_ALREADY_AVAILABLE", "Phòng đang ở trạng thái trống.");
}
