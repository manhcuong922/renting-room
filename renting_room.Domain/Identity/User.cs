using renting_room.Domain.Common;

namespace renting_room.Domain.Identity;

/// <summary>Tài khoản đăng nhập. SystemAdmin không thuộc tổ chức; các role khác thuộc đúng 1 tổ chức (ID-BR-03).</summary>
public sealed class User : AuditableEntity
{
    public const int MaxFailedLoginAttempts = 5;
    public static readonly TimeSpan LockoutDuration = TimeSpan.FromMinutes(15);

    /// <summary>ID-BR-20: mật khẩu tạm (tạo tài khoản / cấp lại) chỉ dùng được trong khoảng này.</summary>
    public static readonly TimeSpan TemporaryPasswordLifetime = TimeSpan.FromHours(72);

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
    public DateTimeOffset? TempPasswordExpiresAt { get; private set; }
    public DateTimeOffset? PasswordChangedAt { get; private set; }
    public DateTimeOffset? RemovedAt { get; private set; }
    public Guid? RemovedBy { get; private set; }

    /// <summary>
    /// ID-BR-22: phó quản lý được chủ trọ cấp quyền xem dữ liệu nhạy cảm (số giấy tờ đầy đủ, xuất file / in có số đầy đủ).
    /// Chủ trọ luôn có quyền — cờ này chỉ có nghĩa với phó quản lý.
    /// </summary>
    public bool CanViewSensitiveData { get; private set; }

    public bool HasSensitiveDataAccess => Role == UserRole.OrgOwner || (Role == UserRole.OrgManager && CanViewSensitiveData);

    /// <summary>Tên đăng nhập hiển thị: ưu tiên SĐT, sau đó email.</summary>
    public string Username => PhoneNormalized ?? EmailNormalized!;

    public static User CreateOrgOwner(
        Guid organizationId, string fullName, string? phone, string? email, string temporaryPasswordHash, DateTimeOffset now) =>
        CreateOrgMember(UserRole.OrgOwner, organizationId, fullName, phone, email, temporaryPasswordHash, now);

    /// <summary>Phó quản lý — thao tác nghiệp vụ như chủ trọ, không quản lý thành viên (ID-BR-14).</summary>
    public static User CreateOrgManager(
        Guid organizationId, string fullName, string? phone, string? email, string temporaryPasswordHash, DateTimeOffset now) =>
        CreateOrgMember(UserRole.OrgManager, organizationId, fullName, phone, email, temporaryPasswordHash, now);

    public static User CreateSystemAdmin(string fullName, string? phone, string? email, string passwordHash)
        => Create(null, UserRole.SystemAdmin, fullName, phone, email, passwordHash, mustChangePassword: false);

    private static User CreateOrgMember(
        UserRole role, Guid organizationId, string fullName, string? phone, string? email, string temporaryPasswordHash, DateTimeOffset now)
    {
        if (organizationId == Guid.Empty)
            throw new ArgumentException("Organization id is required.", nameof(organizationId));

        var user = Create(organizationId, role, fullName, phone, email, temporaryPasswordHash, mustChangePassword: true);
        user.TempPasswordExpiresAt = now.Add(TemporaryPasswordLifetime);
        return user;
    }

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

        var (normalizedPhone, normalizedEmail) = NormalizeContacts(phone, email);

        return new User
        {
            Id = Guid.CreateVersion7(),
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
        Status != UserStatus.Active || (LockoutEnd is not null && LockoutEnd > now);

    public bool IsTemporaryPasswordExpired(DateTimeOffset now) =>
        MustChangePassword && TempPasswordExpiresAt is not null && TempPasswordExpiresAt <= now;

    public void ChangePassword(string newPasswordHash, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(newPasswordHash))
            throw new ArgumentException("Password hash is required.", nameof(newPasswordHash));

        PasswordHash = newPasswordHash;
        MustChangePassword = false;
        TempPasswordExpiresAt = null;
        PasswordChangedAt = now;
        RotateSecurityStamp();
    }

    /// <summary>Cấp lại mật khẩu tạm: bắt buộc đổi, mở khóa tạm do sai mật khẩu, đá mọi phiên đang mở.</summary>
    public Result ResetPassword(string temporaryPasswordHash, DateTimeOffset now)
    {
        if (Status == UserStatus.Removed)
            return Result.Failure(IdentityErrors.UserRemoved);

        PasswordHash = temporaryPasswordHash;
        MustChangePassword = true;
        TempPasswordExpiresAt = now.Add(TemporaryPasswordLifetime);
        FailedLoginCount = 0;
        LockoutEnd = null;
        RotateSecurityStamp();
        return Result.Success();
    }

    public Result Lock()
    {
        if (Status == UserStatus.Removed)
            return Result.Failure(IdentityErrors.UserRemoved);
        if (Status == UserStatus.Locked)
            return Result.Failure(IdentityErrors.UserAlreadyLocked);

        Status = UserStatus.Locked;
        RotateSecurityStamp();
        return Result.Success();
    }

    public Result Unlock()
    {
        if (Status != UserStatus.Locked)
            return Result.Failure(IdentityErrors.UserNotLocked);

        Status = UserStatus.Active;
        FailedLoginCount = 0;
        LockoutEnd = null;
        return Result.Success();
    }

    /// <summary>
    /// Gỡ phó quản lý (ID-BR-17): không xóa vật lý — dữ liệu nghiệp vụ vẫn trỏ tới user này qua created_by.
    /// SĐT/email được giải phóng để có thể dùng cho tài khoản khác.
    /// </summary>
    public Result Remove(DateTimeOffset now, Guid removedBy)
    {
        if (Role != UserRole.OrgManager)
            return Result.Failure(IdentityErrors.CannotModifyOwner);
        if (Status == UserStatus.Removed)
            return Result.Failure(IdentityErrors.UserRemoved);

        Status = UserStatus.Removed;
        CanViewSensitiveData = false;
        RemovedAt = now;
        RemovedBy = removedBy;
        PhoneNormalized = null;
        EmailNormalized = $"removed-{Id:N}@removed.invalid";
        RotateSecurityStamp();
        return Result.Success();
    }

    /// <summary>Chủ trọ cấp / thu hồi quyền xem dữ liệu nhạy cảm của phó quản lý (ID-BR-22).</summary>
    public Result SetSensitiveDataAccess(bool allowed)
    {
        if (Role != UserRole.OrgManager)
            return Result.Failure(IdentityErrors.CannotModifyOwner);
        if (Status == UserStatus.Removed)
            return Result.Failure(IdentityErrors.UserRemoved);

        CanViewSensitiveData = allowed;
        return Result.Success();
    }

    public Result UpdateProfile(string fullName, string? phone, string? email)
    {
        if (Status == UserStatus.Removed)
            return Result.Failure(IdentityErrors.UserRemoved);
        if (string.IsNullOrWhiteSpace(fullName))
            throw new ArgumentException("Full name is required.", nameof(fullName));

        var (normalizedPhone, normalizedEmail) = NormalizeContacts(phone, email);
        var usernameChanged = normalizedPhone != PhoneNormalized || normalizedEmail != EmailNormalized;

        FullName = fullName.Trim();
        PhoneNormalized = normalizedPhone;
        EmailNormalized = normalizedEmail;

        // Đổi tên đăng nhập ⇒ buộc đăng nhập lại.
        if (usernameChanged)
            RotateSecurityStamp();

        return Result.Success();
    }

    /// <summary>Thay password hash khi thuật toán băm được nâng cấp — không ảnh hưởng phiên đăng nhập.</summary>
    public void UpgradePasswordHash(string passwordHash) => PasswordHash = passwordHash;

    /// <summary>Vô hiệu hóa mọi access token đang lưu hành (middleware so khớp claim "stamp").</summary>
    public void RotateSecurityStamp() => SecurityStamp = Guid.NewGuid();

    private static (string? Phone, string? Email) NormalizeContacts(string? phone, string? email)
    {
        var normalizedPhone = ContactNormalizer.NormalizePhone(phone);
        var normalizedEmail = ContactNormalizer.NormalizeEmail(email);
        if (normalizedPhone is null && normalizedEmail is null)
            throw new ArgumentException("A valid phone number or email is required.");

        return (normalizedPhone, normalizedEmail);
    }
}
