namespace renting_room.Domain.Identity;

public enum RefreshTokenRevokeReason
{
    Rotated,
    Logout,
    Reuse,
    PasswordChanged,
    AdminAction
}
