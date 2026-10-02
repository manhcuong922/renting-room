using renting_room.Domain.Common;
using renting_room.Domain.Identity;

namespace renting_room.UnitTests.Domain;

public sealed class OrganizationTests
{
    private static Organization NewOrganization() =>
        Organization.Create(" nt-caugiay ", "Nhà trọ Cầu Giấy", null, "0912345678", null, null, null);

    [Fact]
    public void Create_NormalizesCode_AndStartsActive()
    {
        var organization = NewOrganization();

        organization.Code.Should().Be("NT-CAUGIAY");
        organization.Status.Should().Be(OrganizationStatus.Active);
    }

    [Fact]
    public void Suspend_ThenReactivate_ChangesStatus()
    {
        var organization = NewOrganization();

        organization.Suspend("Chưa thanh toán").IsSuccess.Should().BeTrue();
        organization.Status.Should().Be(OrganizationStatus.Suspended);
        organization.SuspendedReason.Should().Be("Chưa thanh toán");

        organization.Reactivate().IsSuccess.Should().BeTrue();
        organization.Status.Should().Be(OrganizationStatus.Active);
        organization.SuspendedReason.Should().BeNull();
    }

    [Fact]
    public void Suspend_Fails_WhenAlreadySuspended()
    {
        var organization = NewOrganization();
        organization.Suspend("Lý do");

        var result = organization.Suspend("Lần hai");

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(IdentityErrors.OrganizationAlreadySuspended);
    }

    [Fact]
    public void Reactivate_Fails_WhenActive()
    {
        var result = NewOrganization().Reactivate();

        result.Error.Should().Be(IdentityErrors.OrganizationNotSuspended);
    }
}

public sealed class RefreshTokenTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 2, 8, 0, 0, TimeSpan.Zero);

    private static RefreshToken Issue(DateTimeOffset now, TimeSpan lifetime, DateTimeOffset familyExpiresAt) =>
        RefreshToken.Issue(Guid.NewGuid(), Guid.NewGuid(), "hash", Guid.NewGuid(), now, lifetime, familyExpiresAt, "127.0.0.1");

    [Fact]
    public void Issue_SetsSlidingExpiry_FromLifetime()
    {
        var token = Issue(Now, TimeSpan.FromDays(30), familyExpiresAt: Now.AddDays(90));

        token.ExpiresAt.Should().Be(Now.AddDays(30));
        token.FamilyExpiresAt.Should().Be(Now.AddDays(90));
        token.IsRevoked.Should().BeFalse();
        token.IsExpired(Now.AddDays(30).AddTicks(-1)).Should().BeFalse();
        token.IsExpired(Now.AddDays(30)).Should().BeTrue();
        token.WasRotatedWithinGracePeriod(Now).Should().BeFalse();
        token.IsSuspiciousReuse(Now).Should().BeFalse();
    }

    [Fact]
    public void Issue_CapsExpiry_AtFamilyAbsoluteExpiry()
    {
        var token = Issue(Now, TimeSpan.FromDays(30), familyExpiresAt: Now.AddDays(5));

        token.ExpiresAt.Should().Be(Now.AddDays(5));
    }

    [Fact]
    public void Issue_Throws_WhenFamilyAlreadyExpired()
    {
        var act = () => Issue(Now, TimeSpan.FromDays(30), familyExpiresAt: Now);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Issue_Throws_WhenLifetimeNotPositive()
    {
        var act = () => Issue(Now, TimeSpan.Zero, familyExpiresAt: Now.AddDays(90));

        act.Should().Throw<ArgumentOutOfRangeException>();
    }
}

public sealed class ResultTests
{
    [Fact]
    public void ImplicitConversion_FromValue_CreatesSuccess()
    {
        Result<int> result = 42;

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(42);
        result.Error.Should().BeNull();
    }

    [Fact]
    public void ImplicitConversion_FromError_CreatesFailure()
    {
        Result<int> result = IdentityErrors.InvalidCredentials;

        result.IsFailure.Should().BeTrue();
        result.Error!.Code.Should().Be("INVALID_CREDENTIALS");
        result.Error.Type.Should().Be(ErrorType.Unauthorized);
    }
}
