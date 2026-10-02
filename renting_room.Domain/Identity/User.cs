using renting_room.Domain.Common;

namespace renting_room.Domain.Identity;

/// <summary>Tài khoản đăng nhập. SystemAdmin không thuộc tổ chức; các role khác thuộc đúng 1 tổ chức (ID-BR-03).</summary>
public sealed class User : AuditableEntity
{
    public const int MaxFailedLoginAttempts = 5;
    public static readonly TimeSpan LockoutDuration = TimeSpan.FromMinutes(15);

    private User() { } // EF Core

    public Guid? OrganizationId { get; private set; }
    public UserRole Role { get; private set; }
    public string FullName { get; private set; } = null!;
    public string? PhoneNormalized { get; private set; }
    public string? EmailNormalized { get; private set; }
    public string PasswordHash { get; private set; } = null!;
    public Guid SecurityStamp { get; private set; }
    public bool MustChangePassword { get; private set; }
    public UserStatus Status { get; private set; }
    public int FailedLoginCount { get; private set; }
    public DateTimeOffset? LockoutEnd { get; private set; }
    public DateTimeOffset? LastLoginAt { get; private set; }

    /// <summary>Tên đăng nhập hiển thị: ưu tiên SĐT, sau đó email.</summary>
    public string Username => PhoneNormalized ?? EmailNormalized!;

    public static User CreateOrgOwner(
        Guid organizationId, string fullName, string? phone, string? email, string passwordHash)
    {
        if (organizationId == Guid.Empty)
            throw new ArgumentException("Organization id is required.", nameof(organizationId));

        return Create(organizationId, UserRole.OrgOwner, fullName, phone, email, passwordHash, mustChangePassword: true);
    }

    public static User CreateSystemAdmin(string fullName, string? phone, string? email, string passwordHash)
        => Create(null, UserRole.SystemAdmin, fullName, phone, email, passwordHash, mustChangePassword: false);

    private static User Create(
        Guid? organizationId,
        UserRole role,
        string fullName,
        string? phone,
        string? email,
        string passwordHash,
        bool mustChangePassword)
    {
        if (string.IsNullOrWhiteSpace(fullName))
            throw new ArgumentException("Full name is required.", nameof(fullName));
        if (string.IsNullOrWhiteSpace(passwordHash))
            throw new ArgumentException("Password hash is required.", nameof(passwordHash));

        var normalizedPhone = ContactNormalizer.NormalizePhone(phone);
        var normalizedEmail = ContactNormalizer.NormalizeEmail(email);
        if (normalizedPhone is null && normalizedEmail is null)
            throw new ArgumentException("A valid phone number or email is required.");

        return new User
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId,
            Role = role,
            FullName = fullName.Trim(),
            PhoneNormalized = normalizedPhone,
            EmailNormalized = normalizedEmail,
            PasswordHash = passwordHash,
            SecurityStamp = Guid.NewGuid(),
            MustChangePassword = mustChangePassword,
            Status = UserStatus.Active
        };
    }

    public bool IsLockedOut(DateTimeOffset now) =>
        Status == UserStatus.Locked || (LockoutEnd is not null && LockoutEnd > now);

    public void ChangePassword(string newPasswordHash)
    {
        if (string.IsNullOrWhiteSpace(newPasswordHash))
            throw new ArgumentException("Password hash is required.", nameof(newPasswordHash));

        PasswordHash = newPasswordHash;
        MustChangePassword = false;
        RotateSecurityStamp();
    }

    /// <summary>Thay password hash khi thuật toán băm được nâng cấp — không ảnh hưởng phiên đăng nhập.</summary>
    public void UpgradePasswordHash(string passwordHash) => PasswordHash = passwordHash;

    /// <summary>Vô hiệu hóa mọi access token đang lưu hành (middleware so khớp claim "stamp").</summary>
    public void RotateSecurityStamp() => SecurityStamp = Guid.NewGuid();
}
