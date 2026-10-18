namespace renting_room.Application.Common.Security;

/// <summary>Tên claim trong access token — dùng chung giữa nơi phát hành (Infrastructure) và nơi đọc (API).</summary>
public static class AppClaimTypes
{
    public const string Subject = "sub";
    public const string Role = "role";
    public const string OrganizationId = "org";
    public const string SecurityStamp = "stamp";
    public const string MustChangePassword = "pwd_change";
    public const string TokenId = "jti";

    /// <summary>Phiên đăng nhập = family của refresh token; đăng xuất phiên ⇒ access token mang claim này hết hiệu lực ngay.</summary>
    public const string SessionId = "sid";
}
