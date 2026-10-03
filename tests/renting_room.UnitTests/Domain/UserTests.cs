using renting_room.Domain.Identity;

namespace renting_room.UnitTests.Domain;

public sealed class UserTests
{
    private static readonly Guid OrganizationId = Guid.NewGuid();
    private static readonly DateTimeOffset Now = new(2026, 10, 2, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public void CreateOrgOwner_RequiresPasswordChange_AndNormalizesContacts()
    {
        var user = User.CreateOrgOwner(OrganizationId, "  Nguyễn Văn Minh ", "+84 912 345 678", "Minh@Example.com", "hash", Now);

        user.Role.Should().Be(UserRole.OrgOwner);
        user.OrganizationId.Should().Be(OrganizationId);
        user.FullName.Should().Be("Nguyễn Văn Minh");
        user.PhoneNormalized.Should().Be("0912345678");
        user.EmailNormalized.Should().Be("minh@example.com");
        user.Username.Should().Be("0912345678");
        user.MustChangePassword.Should().BeTrue();
        user.Status.Should().Be(UserStatus.Active);
        user.SecurityStamp.Should().NotBeEmpty();
    }

    [Fact]
    public void CreateOrgOwner_Throws_WhenNoValidPhoneOrEmail()
    {
        var act = () => User.CreateOrgOwner(OrganizationId, "Minh", "123", "invalid", "hash", Now);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void CreateOrgOwner_Throws_WhenOrganizationMissing()
    {
        var act = () => User.CreateOrgOwner(Guid.Empty, "Minh", "0912345678", null, "hash", Now);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void CreateSystemAdmin_HasNoOrganization_AndNoForcedPasswordChange()
    {
        var admin = User.CreateSystemAdmin("Admin", null, "admin@example.com", "hash");

        admin.OrganizationId.Should().BeNull();
        admin.Role.Should().Be(UserRole.SystemAdmin);
        admin.MustChangePassword.Should().BeFalse();
        admin.Username.Should().Be("admin@example.com");
    }

    [Fact]
    public void ChangePassword_ClearsForcedChange_AndRotatesSecurityStamp()
    {
        var user = User.CreateOrgOwner(OrganizationId, "Minh", "0912345678", null, "old-hash", Now);
        var originalStamp = user.SecurityStamp;

        user.ChangePassword("new-hash", Now);

        user.PasswordHash.Should().Be("new-hash");
        user.MustChangePassword.Should().BeFalse();
        user.SecurityStamp.Should().NotBe(originalStamp);
    }

    [Fact]
    public void UpgradePasswordHash_DoesNotRotateSecurityStamp()
    {
        var user = User.CreateOrgOwner(OrganizationId, "Minh", "0912345678", null, "old-hash", Now);
        var originalStamp = user.SecurityStamp;

        user.UpgradePasswordHash("rehashed");

        user.SecurityStamp.Should().Be(originalStamp);
    }

    [Fact]
    public void IsLockedOut_IsFalse_ForNewUser()
    {
        var user = User.CreateOrgOwner(OrganizationId, "Minh", "0912345678", null, "hash", Now);

        user.IsLockedOut(DateTimeOffset.UtcNow).Should().BeFalse();
    }

    [Fact]
    public void TemporaryPassword_ExpiresAfter72Hours_AndClearsAfterChange()
    {
        var user = User.CreateOrgOwner(OrganizationId, "Minh", "0912345678", null, "hash", Now);

        user.TempPasswordExpiresAt.Should().Be(Now.AddHours(72));
        user.IsTemporaryPasswordExpired(Now.AddHours(71)).Should().BeFalse();
        user.IsTemporaryPasswordExpired(Now.AddHours(72)).Should().BeTrue();

        user.ChangePassword("new-hash", Now.AddHours(1));
        user.TempPasswordExpiresAt.Should().BeNull();
        user.PasswordChangedAt.Should().Be(Now.AddHours(1));
        user.IsTemporaryPasswordExpired(Now.AddDays(10)).Should().BeFalse();
    }

    [Fact]
    public void Manager_CanBeLockedUnlockedAndRemoved_AndRemovalFreesPhone()
    {
        var manager = User.CreateOrgManager(OrganizationId, "Phó", "0912345678", null, "hash", Now);
        var stamp = manager.SecurityStamp;

        manager.Lock().IsSuccess.Should().BeTrue();
        manager.IsLockedOut(Now).Should().BeTrue();
        manager.SecurityStamp.Should().NotBe(stamp);
        manager.Lock().Error.Should().Be(IdentityErrors.UserAlreadyLocked);
        manager.Unlock().IsSuccess.Should().BeTrue();

        var removedBy = Guid.NewGuid();
        manager.Remove(Now, removedBy).IsSuccess.Should().BeTrue();
        manager.Status.Should().Be(UserStatus.Removed);
        manager.PhoneNormalized.Should().BeNull();
        manager.RemovedBy.Should().Be(removedBy);
        manager.IsLockedOut(Now).Should().BeTrue();
        manager.Unlock().Error.Should().Be(IdentityErrors.UserNotLocked);
        manager.ResetPassword("x", Now).Error.Should().Be(IdentityErrors.UserRemoved);
    }

    [Fact]
    public void Owner_CannotBeRemoved()
    {
        var owner = User.CreateOrgOwner(OrganizationId, "Chủ", "0912345678", null, "hash", Now);

        owner.Remove(Now, Guid.NewGuid()).Error.Should().Be(IdentityErrors.CannotModifyOwner);
    }

    [Fact]
    public void ResetPassword_RequiresChange_RotatesStamp_AndClearsLockout()
    {
        var user = User.CreateOrgOwner(OrganizationId, "Minh", "0912345678", null, "hash", Now);
        user.ChangePassword("own", Now);
        var stamp = user.SecurityStamp;

        user.ResetPassword("temp", Now.AddDays(1)).IsSuccess.Should().BeTrue();

        user.MustChangePassword.Should().BeTrue();
        user.TempPasswordExpiresAt.Should().Be(Now.AddDays(1).AddHours(72));
        user.SecurityStamp.Should().NotBe(stamp);
    }
}
