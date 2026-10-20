namespace renting_room.Domain.Billing;

public sealed record CalculatedSegment(
    Guid FeeTypeId, Guid MeterId, string? MeterSerial, Guid StartReadingId, Guid EndReadingId, decimal StartValue, decimal EndValue,
    Guid? RoomId = null, string? RoomCode = null, DateOnly? StartDate = null, DateOnly? EndDate = null, DateOnly? UsageStart = null);
