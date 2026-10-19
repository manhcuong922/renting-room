namespace renting_room.Requests;

public sealed record CreateManagerRequest(string FullName, string? Phone, string? Email);

public sealed record UpdateManagerRequest(string FullName, string? Phone, string? Email, uint Version);

public sealed record SensitiveDataAccessRequest(bool Allowed);

public sealed record WriteOffPermissionRequest(bool Allowed);
