using renting_room.Domain.Identity;

namespace renting_room.UnitTests.Domain;

public sealed class UserTests
{
    private static readonly Guid OrganizationId = Guid.NewGuid();

    [Fact]
    public void CreateOrgOwner_RequiresPasswordChange_AndNormalizesContacts()
    {
        var user = User.CreateOrgOwner(OrganizationId, "  Nguyễn Văn Minh ", "+84 912 345 678", "Minh@Example.com", "hash");

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
        var act = () => User.CreateOrgOwner(OrganizationId, "Minh", "123", "invalid", "hash");

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void CreateOrgOwner_Throws_WhenOrganizationMissing()
    {
        var act = () => User.CreateOrgOwner(Guid.Empty, "Minh", "0912345678", null, "hash");

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
        var user = User.CreateOrgOwner(OrganizationId, "Minh", "0912345678", null, "old-hash");
        var originalStamp = user.SecurityStamp;

        user.ChangePassword("new-hash");

        user.PasswordHash.Should().Be("new-hash");
        user.MustChangePassword.Should().BeFalse();
        user.SecurityStamp.Should().NotBe(originalStamp);
    }

    [Fact]
    public void UpgradePasswordHash_DoesNotRotateSecurityStamp()
    {
        var user = User.CreateOrgOwner(OrganizationId, "Minh", "0912345678", null, "old-hash");
        var originalStamp = user.SecurityStamp;

        user.UpgradePasswordHash("rehashed");

        user.SecurityStamp.Should().Be(originalStamp);
    }

    [Fact]
    public void IsLockedOut_IsFalse_ForNewUser()
    {
        var user = User.CreateOrgOwner(OrganizationId, "Minh", "0912345678", null, "hash");

        user.IsLockedOut(DateTimeOffset.UtcNow).Should().BeFalse();
    }
}
