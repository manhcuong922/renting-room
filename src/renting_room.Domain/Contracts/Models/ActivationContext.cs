namespace renting_room.Domain.Contracts;

public sealed record ActivationContext(
    DateOnly Today,
    DateTimeOffset Now,
    bool RoomAvailable,
    string? SigningSnapshotJson,
    string? HouseRulesSnapshot,
    string? UtilityPriceSnapshotJson = null);
