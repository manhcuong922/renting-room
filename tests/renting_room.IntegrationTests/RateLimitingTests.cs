using System.Net;
using renting_room.IntegrationTests.Infrastructure;

namespace renting_room.IntegrationTests;

/// <summary>Chống spam: chạy trên factory riêng với hạn mức thấp. Mỗi test dùng IP giả lập riêng để không ảnh hưởng nhau.</summary>
[Collection(RateLimitedCollection.Name)]
public sealed class RateLimitingTests(RateLimitedApiFactory factory)
{
    private readonly HttpClient _client = factory.CreateClient();

    /// <summary>Mỗi lần thử một tài khoản khác ⇒ chỉ giới hạn theo IP tác động (không dính giới hạn theo tài khoản).</summary>
    private static string DistinctPhone(int i) => $"09111111{i:D2}";

    [Fact]
    public async Task Login_Returns429_AfterLimitPerIp_WithRetryAfter_ButOtherIpsUnaffected()
    {
        const string attackerIp = "203.0.113.10";

        for (var i = 0; i < RateLimitedApiFactory.LoginLimit; i++)
        {
            var attempt = await _client.PostJsonAsync("/api/v1/auth/login",
                new { username = DistinctPhone(i), password = "guess-123" }, idempotencyKey: null, clientIp: attackerIp);
            attempt.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        }

        var blocked = await _client.PostJsonAsync("/api/v1/auth/login",
            new { username = DistinctPhone(99), password = "guess-123" }, idempotencyKey: null, clientIp: attackerIp);

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
                new { username = DistinctPhone(i), password = "guess-123" }, idempotencyKey: null, clientIp: $"2001:db8:1:1::{i + 1}");
        }

        var blocked = await _client.PostJsonAsync("/api/v1/auth/login",
            new { username = DistinctPhone(99), password = "guess-123" }, idempotencyKey: null, clientIp: "2001:db8:1:1::ffff");

        blocked.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
    }

    [Fact]
    public async Task Login_SameAccountFromOneIp_Returns429_AfterPerAccountLimit_OtherAccountsStillAllowed()
    {
        const string ip = "203.0.113.40";
        Task<HttpResponseMessage> LoginAsync(string username) => _client.PostJsonAsync("/api/v1/auth/login",
            new { username, password = "guess-123" }, idempotencyKey: null, clientIp: ip);

        for (var i = 0; i < RateLimitedApiFactory.LoginPerAccountLimit; i++)
            (await LoginAsync("0922222222")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        var blocked = await LoginAsync("0922222222");
        blocked.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        (await blocked.ReadProblemCodeAsync()).Should().Be("TOO_MANY_REQUESTS");
        blocked.Headers.RetryAfter.Should().NotBeNull();

        (await LoginAsync("+84 922 222 222")).StatusCode.Should().Be(HttpStatusCode.TooManyRequests, "viết SĐT kiểu khác vẫn là cùng tài khoản");
        (await LoginAsync("0933333333")).StatusCode.Should().Be(HttpStatusCode.Unauthorized, "tài khoản khác từ cùng IP chưa vượt giới hạn IP");
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

    /// <summary>Endpoint trả số giấy tờ đầy đủ dùng chung hạn mức "sensitive" theo user — chặn cào CCCD hàng loạt.</summary>
    [Theory]
    [InlineData("renter", "198.51.100.41")]
    [InlineData("lessor", "198.51.100.42")]
    [InlineData("export", "198.51.100.43")]
    public async Task IdNumberRevealingEndpoints_AreLimitedPerUser(string endpoint, string ip)
    {
        var owner = await CreateOwnerAsync(ip);  // đã dùng 1 lượt "sensitive" khi đổi mật khẩu tạm
        var token = owner.Tokens.AccessToken;
        var (url, body) = endpoint switch
        {
            "renter" => ($"/api/v1/renters/{await _client.CreateRenterAsync(token)}/reveal-id-number", (object?)null),
            "lessor" => ($"/api/v1/properties/{await _client.CreatePropertyAsync(token)}/lessor/reveal-id-number", null),
            _ => ("/api/v1/exports/renters", new { includeSensitive = true })
        };

        (await _client.PostJsonAsync(url, body, token, idempotencyKey: null)).StatusCode.Should().Be(HttpStatusCode.OK);

        var blocked = await _client.PostJsonAsync(url, body, token, idempotencyKey: null);
        blocked.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        (await blocked.ReadProblemCodeAsync()).Should().Be("TOO_MANY_REQUESTS");
    }

    [Fact]
    public async Task WriteRequests_AreLimited_ButReadsStillAllowed()
    {
        var owner = await CreateOwnerAsync("198.51.100.30");
        var propertyId = await _client.CreatePropertyAsync(owner.Tokens.AccessToken, withLessor: false);

        HttpResponseMessage? rejected = null;
        for (var i = 0; i < RateLimitedApiFactory.WriteLimit + 1 && rejected is null; i++)
        {
            var response = await _client.PostJsonAsync($"/api/v1/properties/{propertyId}/rooms",
                new { code = $"P{i}", spec = new { maxOccupants = 2 } }, owner.Tokens.AccessToken);
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
