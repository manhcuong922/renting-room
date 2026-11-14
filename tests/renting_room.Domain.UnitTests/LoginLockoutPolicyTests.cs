using renting_room.Domain.Identity;

namespace renting_room.Domain.UnitTests;

/// <summary>M01 ID-BR-06 (05/11/2026, L-13): khóa đăng nhập theo IP + tài khoản, trần chung cho IP lạ.</summary>
public sealed class LoginLockoutPolicyTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 5, 8, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(4, true, 0, false)]
    [InlineData(5, true, 0, true)]   // IP quen cũng bị chặn nếu chính nó sai 5 lần
    [InlineData(0, false, 29, false)]
    [InlineData(0, false, 30, true)] // IP lạ: trần chung 30 lần / giờ
    [InlineData(0, true, 100, false)] // IP quen không bị trần IP lạ chặn
    public void IsBlocked(int fromIp, bool known, int fromUnknown, bool blocked) =>
        LoginLockoutPolicy.IsBlocked(new LoginFailureStats(fromIp, known, fromUnknown)).Should().Be(blocked);

    [Fact]
    public void CredentialsChangedAt_IsLatestOfOwnChangeAndTemporaryIssue()
    {
        var user = User.CreateOrgOwner(Guid.NewGuid(), "Minh", "0912345678", null, "hash", Now);
        user.CredentialsChangedAt.Should().Be(Now, "mật khẩu tạm cấp lúc tạo");

        user.ChangePassword("own", Now.AddDays(1));
        user.CredentialsChangedAt.Should().Be(Now.AddDays(1));

        user.ResetPassword("temp", Now.AddDays(3));
        user.CredentialsChangedAt.Should().Be(Now.AddDays(3), "cấp lại mật khẩu tạm sau lần tự đổi");
    }
}
