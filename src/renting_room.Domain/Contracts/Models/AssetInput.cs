namespace renting_room.Domain.Contracts;

public sealed record AssetInput(string Name, int Quantity, string? ConditionAtHandover, decimal? ValueEstimate, string? Note);
