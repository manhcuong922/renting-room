namespace renting_room.Domain.Contracts;

/// <summary>Dữ liệu Application thu thập để kiểm tra & snapshot khi kích hoạt (CT-BR-02, 18, 19).</summary>
public sealed record ActivationContext(
    DateOnly Today,
    DateTimeOffset Now,
    bool RoomAvailable,
    string? SigningSnapshotJson,
    string? HouseRulesSnapshot,
    string? UtilityPriceSnapshotJson = null);
