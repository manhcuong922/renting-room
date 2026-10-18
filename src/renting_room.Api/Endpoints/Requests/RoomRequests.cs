using renting_room.Application.Rooms;

namespace renting_room.Endpoints;

public sealed record CreateRoomRequest(string Code, RoomSpecInput Spec);

public sealed record BulkCreateRoomsRequest(
    IReadOnlyList<BulkRoomFloor> Floors,
    int? MaxOccupants,
    decimal? AreaM2,
    decimal? ListedRent,
    decimal? DefaultDeposit,
    IReadOnlyCollection<string>? Amenities);

public sealed record UpdateRoomRequest(string Code, RoomSpecInput Spec, uint Version);

public sealed record MaintenanceRequest(string? Note);

public sealed record RoomGroupRequest(string Name, string? Description);

public sealed record RoomGroupMembersRequest(IReadOnlyList<Guid> RoomIds);
