namespace renting_room.Application.Rooms;

public sealed record RoomGroupDto(Guid Id, Guid PropertyId, string Name, string? Description, IReadOnlyList<Guid> RoomIds);
