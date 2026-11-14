namespace renting_room.Requests;

/// <param name="PropertyIds">Khu được giao (M01 ID-BR-24); không gửi = mọi khu đang dùng, <c>[]</c> = chưa giao khu nào.</param>
public sealed record CreateManagerRequest(string FullName, string? Phone, string? Email, IReadOnlyList<Guid>? PropertyIds = null);

public sealed record UpdateManagerRequest(string FullName, string? Phone, string? Email, uint Version);

public sealed record AssignPropertiesRequest(IReadOnlyList<Guid> PropertyIds);
