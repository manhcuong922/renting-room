using System.Security.Cryptography;
using System.Text;
using renting_room.Domain.Common;

namespace renting_room.Domain.Identity;

public enum LoginOutcome
{
    Success,
    UnknownUser,
    WrongPassword,
    LockedOut,
    TemporaryPasswordExpired,
    OrganizationSuspended
}

/// <summary>
/// Nhật ký đăng nhập (M01 §11): mỗi lần đăng nhập thành công / thất bại 1 dòng. Không lưu tên đăng nhập gõ vào — chỉ SHA-256 của tên đã
/// chuẩn hóa (để đếm số lần thử trên cùng một tên mà không lộ SĐT / email của người gõ nhầm).
/// </summary>
public sealed class LoginEvent : Entity
{
    private LoginEvent() { } // EF Core

    public Guid? UserId { get; private set; }
    public Guid? OrganizationId { get; private set; }
    public string UsernameHash { get; private set; } = null!;
    public string? IpAddress { get; private set; }
    public bool Success { get; private set; }
    public LoginOutcome Reason { get; private set; }
    public DateTimeOffset OccurredAt { get; private set; }

    public static LoginEvent Record(User? user, string username, string? ipAddress, LoginOutcome outcome, DateTimeOffset now) => new()
    {
        Id = Guid.CreateVersion7(),
        UserId = user?.Id,
        OrganizationId = user?.OrganizationId,
        UsernameHash = HashUsername(username),
        IpAddress = ipAddress is { Length: > 45 } ? ipAddress[..45] : ipAddress,
        Success = outcome == LoginOutcome.Success,
        Reason = outcome,
        OccurredAt = now
    };

    public static string HashUsername(string username)
    {
        var normalized = (username.Contains('@') ? ContactNormalizer.NormalizeEmail(username) : ContactNormalizer.NormalizePhone(username))
            ?? username.Trim().ToLowerInvariant();
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalized))).ToLowerInvariant();
    }
}
