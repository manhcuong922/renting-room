using System.Net;
using renting_room.IntegrationTests.Infrastructure;

namespace renting_room.IntegrationTests;

[Collection(ApiCollection.Name)]
public sealed class AuthFlowTests(ApiFactory factory)
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task Login_ReturnsTokenPair_ForSeededAdmin_AndAcceptsInternationalPhoneFormat()
    {
        var response = await _client.PostJsonAsync("/api/v1/auth/login",
            new { username = "+84 900 000 099", password = ApiFactory.AdminPassword });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Headers.CacheControl!.NoStore.Should().BeTrue();
        var tokens = await response.ReadAsync<TokenResponse>();
        tokens.AccessToken.Should().NotBeNullOrEmpty();
        tokens.RefreshToken.Should().NotBeNullOrEmpty();
        tokens.MustChangePassword.Should().BeFalse();
    }

    [Fact]
    public async Task Login_Returns401_WithSameError_ForWrongPasswordAndUnknownUser()
    {
        var wrongPassword = await _client.PostJsonAsync("/api/v1/auth/login",
            new { username = ApiFactory.AdminPhone, password = "wrong-password1" });
        var unknownUser = await _client.PostJsonAsync("/api/v1/auth/login",
            new { username = "0911111111", password = "whatever123" });

        wrongPassword.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        unknownUser.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await wrongPassword.ReadProblemCodeAsync()).Should().Be("INVALID_CREDENTIALS");
        (await unknownUser.ReadProblemCodeAsync()).Should().Be("INVALID_CREDENTIALS");
    }

    [Fact]
    public async Task Login_LocksAccount_AfterFiveFailedAttempts_WithoutRevealingLockOrPassword()
    {
        var admin = await _client.LoginAdminAsync();
        var phone = TestApi.NewPhone();
        var organization = await _client.CreateOrganizationAsync(admin.AccessToken, phone);

        for (var attempt = 0; attempt < 5; attempt++)
        {
            var failed = await _client.PostJsonAsync("/api/v1/auth/login", new { username = phone, password = "wrong-pass1" });
            failed.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        }

        // Mật khẩu ĐÚNG nhưng đang bị khóa: phản hồi giống hệt sai mật khẩu — không lộ tài khoản bị khóa,
        // không cho kẻ tấn công biết đã đoán trúng mật khẩu.
        var correctWhileLocked = await _client.PostJsonAsync("/api/v1/auth/login",
            new { username = phone, password = organization.TemporaryPassword });
        correctWhileLocked.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await correctWhileLocked.ReadProblemCodeAsync()).Should().Be("INVALID_CREDENTIALS");
    }

    [Fact]
    public async Task ChangePassword_WrongCurrentPasswordAttempts_LockAccount()
    {
        var owner = await _client.CreateActiveOwnerAsync();

        for (var attempt = 0; attempt < 5; attempt++)
        {
            var wrong = await _client.PostJsonAsync("/api/v1/auth/change-password",
                new { currentPassword = $"guess-{attempt}-x1", newPassword = "MatKhauMoi2026" }, owner.Tokens.AccessToken);
            (await wrong.ReadProblemCodeAsync()).Should().Be("INVALID_CURRENT_PASSWORD");
        }

        var locked = await _client.PostJsonAsync("/api/v1/auth/change-password",
            new { currentPassword = owner.Password, newPassword = "MatKhauMoi2026" }, owner.Tokens.AccessToken);

        locked.StatusCode.Should().Be(HttpStatusCode.Locked);
        (await locked.ReadProblemCodeAsync()).Should().Be("ACCOUNT_LOCKED");
    }

    [Fact]
    public async Task NewOwner_MustChangePassword_BeforeUsingBusinessApis()
    {
        var admin = await _client.LoginAdminAsync();
        var phone = TestApi.NewPhone();
        var organization = await _client.CreateOrganizationAsync(admin.AccessToken, phone);

        var firstLogin = await _client.LoginAsync(phone, organization.TemporaryPassword);
        firstLogin.MustChangePassword.Should().BeTrue();

        var blocked = await _client.GetAsync("/api/v1/rooms", firstLogin.AccessToken);
        blocked.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await blocked.ReadProblemCodeAsync()).Should().Be("PASSWORD_CHANGE_REQUIRED");

        (await _client.GetAsync("/api/v1/me", firstLogin.AccessToken)).StatusCode.Should().Be(HttpStatusCode.OK);

        var changed = await _client.PostJsonAsync("/api/v1/auth/change-password",
            new { currentPassword = organization.TemporaryPassword, newPassword = TestApi.OwnerPassword }, firstLogin.AccessToken);
        var tokens = await changed.ReadAsync<TokenResponse>();

        tokens.MustChangePassword.Should().BeFalse();
        (await _client.GetAsync("/api/v1/rooms", tokens.AccessToken)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task ChangePassword_RevokesPreviousAccessAndRefreshTokens()
    {
        var owner = await _client.CreateActiveOwnerAsync();
        var otherSession = await _client.LoginAsync(owner.Phone, owner.Password);

        var changed = await _client.PostJsonAsync("/api/v1/auth/change-password",
            new { currentPassword = owner.Password, newPassword = "MatKhauMoi2026" }, owner.Tokens.AccessToken);
        changed.StatusCode.Should().Be(HttpStatusCode.OK);

        var oldAccess = await _client.GetAsync("/api/v1/me", otherSession.AccessToken);
        oldAccess.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await oldAccess.ReadProblemCodeAsync()).Should().Be("SESSION_REVOKED");

        var oldRefresh = await _client.PostJsonAsync("/api/v1/auth/refresh", new { refreshToken = otherSession.RefreshToken });
        (await oldRefresh.ReadProblemCodeAsync()).Should().Be("INVALID_REFRESH_TOKEN");

        // Client cũ gửi lại token đã thu hồi KHÔNG được làm mất phiên mới hợp lệ (không phải dấu hiệu đánh cắp).
        factory.Clock.Advance(TimeSpan.FromSeconds(1));
        var newTokens = await changed.ReadAsync<TokenResponse>();
        (await _client.PostJsonAsync("/api/v1/auth/refresh", new { refreshToken = owner.Tokens.RefreshToken }))
            .StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await _client.GetAsync("/api/v1/me", newTokens.AccessToken)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task ChangePassword_Returns422_WhenCurrentPasswordWrong()
    {
        var owner = await _client.CreateActiveOwnerAsync();

        var response = await _client.PostJsonAsync("/api/v1/auth/change-password",
            new { currentPassword = "not-my-password1", newPassword = "MatKhauMoi2026" }, owner.Tokens.AccessToken);

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await response.ReadProblemCodeAsync()).Should().Be("INVALID_CURRENT_PASSWORD");
    }

    [Fact]
    public async Task Refresh_RotatesToken_AndDetectsReuseAfterGracePeriod()
    {
        var owner = await _client.CreateActiveOwnerAsync();
        var original = owner.Tokens;

        var rotated = await (await _client.PostJsonAsync("/api/v1/auth/refresh", new { refreshToken = original.RefreshToken }))
            .ReadAsync<TokenResponse>();
        rotated.RefreshToken.Should().NotBe(original.RefreshToken);

        // Dùng lại ngay (giống 2 tab gọi song song): bị từ chối nhưng KHÔNG thu hồi phiên.
        var reusedWithinGrace = await _client.PostJsonAsync("/api/v1/auth/refresh", new { refreshToken = original.RefreshToken });
        reusedWithinGrace.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        var next = await _client.PostJsonAsync("/api/v1/auth/refresh", new { refreshToken = rotated.RefreshToken });
        next.StatusCode.Should().Be(HttpStatusCode.OK);
        var latest = await next.ReadAsync<TokenResponse>();

        // Dùng lại sau grace period: nghi bị đánh cắp → thu hồi cả family, token mới nhất cũng mất hiệu lực.
        factory.Clock.Advance(TimeSpan.FromSeconds(11));
        var reusedLater = await _client.PostJsonAsync("/api/v1/auth/refresh", new { refreshToken = original.RefreshToken });
        reusedLater.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        var afterTheftDetection = await _client.PostJsonAsync("/api/v1/auth/refresh", new { refreshToken = latest.RefreshToken });
        afterTheftDetection.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        // Access token đang lưu hành (có thể đang trong tay kẻ gian) cũng bị vô hiệu ngay.
        var accessAfterTheft = await _client.GetAsync("/api/v1/me", latest.AccessToken);
        (await accessAfterTheft.ReadProblemCodeAsync()).Should().Be("SESSION_REVOKED");
    }

    [Fact]
    public async Task Refresh_ConcurrentRequestsWithSameToken_OnlyOneSucceeds()
    {
        var owner = await _client.CreateActiveOwnerAsync();

        var responses = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ =>
            factory.CreateClient().PostJsonAsync("/api/v1/auth/refresh", new { refreshToken = owner.Tokens.RefreshToken })));

        responses.Count(r => r.StatusCode == HttpStatusCode.OK).Should().Be(1);
        responses.Count(r => r.StatusCode == HttpStatusCode.Unauthorized).Should().Be(7);

        var winner = await responses.Single(r => r.StatusCode == HttpStatusCode.OK).ReadAsync<TokenResponse>();
        var stillValid = await _client.PostJsonAsync("/api/v1/auth/refresh", new { refreshToken = winner.RefreshToken });
        stillValid.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Logout_RevokesRefreshToken()
    {
        var owner = await _client.CreateActiveOwnerAsync();

        var logout = await _client.PostJsonAsync("/api/v1/auth/logout", new { refreshToken = owner.Tokens.RefreshToken });
        logout.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var refresh = await _client.PostJsonAsync("/api/v1/auth/refresh", new { refreshToken = owner.Tokens.RefreshToken });
        refresh.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Logout_WithUnknownToken_StillReturns204()
    {
        var response = await _client.PostJsonAsync("/api/v1/auth/logout", new { refreshToken = "does-not-exist" });

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task LogoutAll_InvalidatesCurrentAccessToken()
    {
        var owner = await _client.CreateActiveOwnerAsync();

        var response = await _client.PostJsonAsync("/api/v1/auth/logout-all", null, owner.Tokens.AccessToken);
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var me = await _client.GetAsync("/api/v1/me", owner.Tokens.AccessToken);
        (await me.ReadProblemCodeAsync()).Should().Be("SESSION_REVOKED");
    }
}
