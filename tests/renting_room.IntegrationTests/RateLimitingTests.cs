using System.Net;
using renting_room.IntegrationTests.Infrastructure;

namespace renting_room.IntegrationTests;

/// <summary>Chống spam: chạy trên factory riêng với hạn mức thấp. Mỗi test dùng IP giả lập riêng để không ảnh hưởng nhau.</summary>
[Collection(RateLimitedCollection.Name)]
public sealed class RateLimitingTests(RateLimitedApiFactory factory)
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task Login_Returns429_AfterLimitPerIp_WithRetryAfter_ButOtherIpsUnaffected()
    {
        const string attackerIp = "203.0.113.10";

        for (var i = 0; i < RateLimitedApiFactory.LoginLimit; i++)
        {
            var attempt = await _client.PostJsonAsync("/api/v1/auth/login",
                new { username = "0911111111", password = "guess-123" }, idempotencyKey: null, clientIp: attackerIp);
            attempt.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        }

        var blocked = await _client.PostJsonAsync("/api/v1/auth/login",
            new { username = "0911111111", password = "guess-123" }, idempotencyKey: null, clientIp: attackerIp);

        blocked.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        (await blocked.ReadProblemCodeAsync()).Should().Be("TOO_MANY_REQUESTS");
        blocked.Headers.RetryAfter.Should().NotBeNull();

        var otherClient = await _client.PostJsonAsync("/api/v1/auth/login",
            new { username = ApiFactory.AdminPhone, password = ApiFactory.AdminPassword }, idempotencyKey: null, clientIp: "203.0.113.11");
        otherClient.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Ipv6Clients_InSame64Prefix_ShareOneLoginLimit()
    {
        for (var i = 0; i < RateLimitedApiFactory.LoginLimit; i++)
        {
            // Mỗi lần một địa chỉ khác nhau nhưng cùng dải /64 — không được lách giới hạn.
            await _client.PostJsonAsync("/api/v1/auth/login",
                new { username = "0911111111", password = "guess-123" }, idempotencyKey: null, clientIp: $"2001:db8:1:1::{i + 1}");
        }

        var blocked = await _client.PostJsonAsync("/api/v1/auth/login",
            new { username = "0911111111", password = "guess-123" }, idempotencyKey: null, clientIp: "2001:db8:1:1::ffff");

        blocked.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
    }

    [Fact]
    public async Task SensitiveEndpoint_ChangePassword_IsLimitedPerUser()
    {
        var owner = await CreateOwnerAsync("198.51.100.20");  // đã dùng 1 lượt "sensitive" khi đổi mật khẩu tạm

        var second = await _client.PostJsonAsync("/api/v1/auth/change-password",
            new { currentPassword = "wrong-pass-1", newPassword = "MatKhauMoi2026" }, owner.Tokens.AccessToken, idempotencyKey: null);
        second.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);

        var third = await _client.PostJsonAsync("/api/v1/auth/change-password",
            new { currentPassword = "wrong-pass-2", newPassword = "MatKhauMoi2026" }, owner.Tokens.AccessToken, idempotencyKey: null);
        third.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
    }

    [Fact]
    public async Task WriteRequests_AreLimited_ButReadsStillAllowed()
    {
        var owner = await CreateOwnerAsync("198.51.100.30");

        HttpResponseMessage? rejected = null;
        for (var i = 0; i < RateLimitedApiFactory.WriteLimit + 1 && rejected is null; i++)
        {
            var response = await _client.PostJsonAsync("/api/v1/rooms", new { name = $"P{i}", monthlyRent = 1_000_000 }, owner.Tokens.AccessToken);
            if (response.StatusCode == HttpStatusCode.TooManyRequests)
                rejected = response;
        }

        rejected.Should().NotBeNull("spam tạo dữ liệu phải bị chặn trong giới hạn ghi");
        (await _client.GetAsync("/api/v1/rooms", owner.Tokens.AccessToken)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    /// <summary>Tạo chủ trọ với IP riêng (login bị giới hạn theo IP, nên mỗi test dùng IP khác nhau).</summary>
    private async Task<OwnerAccount> CreateOwnerAsync(string ip)
    {
        var adminLogin = await _client.PostJsonAsync("/api/v1/auth/login",
            new { username = ApiFactory.AdminPhone, password = ApiFactory.AdminPassword }, idempotencyKey: null, clientIp: ip);
        var admin = await adminLogin.ReadAsync<TokenResponse>();

        var phone = TestApi.NewPhone();
        var organization = await _client.CreateOrganizationAsync(admin.AccessToken, phone);

        var ownerLogin = await _client.PostJsonAsync("/api/v1/auth/login",
            new { username = phone, password = organization.TemporaryPassword }, idempotencyKey: null, clientIp: ip);
        var firstTokens = await ownerLogin.ReadAsync<TokenResponse>();

        var changed = await _client.PostJsonAsync("/api/v1/auth/change-password",
            new { currentPassword = organization.TemporaryPassword, newPassword = TestApi.OwnerPassword }, firstTokens.AccessToken, idempotencyKey: null);
        changed.StatusCode.Should().Be(HttpStatusCode.OK);

        return new OwnerAccount(organization, phone, TestApi.OwnerPassword, await changed.ReadAsync<TokenResponse>());
    }
}
