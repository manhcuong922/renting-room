namespace renting_room.Application.Meters;

public sealed record BulkInstallRow(Guid RoomId, string? SerialNo, decimal InitialValue);
