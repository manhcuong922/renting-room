namespace renting_room.Application.Meters;

// MT-UC-01: lắp công tơ hàng loạt cho nhiều phòng của khu
public sealed record BulkInstallRow(Guid RoomId, string? SerialNo, decimal InitialValue);
