namespace renting_room.Domain.Properties;

public sealed record RoomSpec(
    string? Floor,
    decimal? AreaM2,
    int? MaxOccupants,
    decimal? ListedRent,
    decimal? DefaultDeposit,
    IReadOnlyCollection<string> Amenities,
    string? Description);
