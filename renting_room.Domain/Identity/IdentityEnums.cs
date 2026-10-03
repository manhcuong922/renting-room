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
    Locked,

    /// <summary>Phó quản lý đã bị gỡ khỏi tổ chức — không đăng nhập, không khôi phục (ID-BR-17).</summary>
    Removed
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
