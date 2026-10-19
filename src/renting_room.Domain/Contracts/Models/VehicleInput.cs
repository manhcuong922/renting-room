namespace renting_room.Domain.Contracts;

public sealed record VehicleInput(Guid? RenterId, VehicleType VehicleType, string? PlateNumber, string? BrandColor, DateOnly RegisteredFrom, string? Note);
