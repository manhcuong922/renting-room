namespace renting_room.Domain.Identity;

public enum UserRole
{
    SystemAdmin,
    OrgOwner,
    OrgManager
}

public enum UserStatus
{
    Active,
    Locked
}

public enum OrganizationStatus
{
    Active,
    Suspended
}

public enum RefreshTokenRevokeReason
{
    Rotated,
    Logout,
    Reuse,
    PasswordChanged,
    AdminAction
}
