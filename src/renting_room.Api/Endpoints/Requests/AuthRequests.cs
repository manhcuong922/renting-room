namespace renting_room.Endpoints;

public sealed record LoginRequest(string Username, string Password);

public sealed record RefreshTokenRequest(string RefreshToken);

public sealed record ChangePasswordRequest(string CurrentPassword, string NewPassword);

public sealed record UpdateMeRequest(string FullName, string? Phone, string? Email, string? CurrentPassword = null);
