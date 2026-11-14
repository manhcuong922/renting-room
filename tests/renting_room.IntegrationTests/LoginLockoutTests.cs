using System.Net;
using renting_room.IntegrationTests.Infrastructure;

namespace renting_room.IntegrationTests;

/// <summary>
/// 05/11/2026 — L-13 (M01 ID-BR-06): khóa đăng nhập theo IP + tài khoản. Kẻ biết SĐT chủ trọ gõ sai liên tục chỉ khóa được chính IP của họ;
/// dò bằng nhiều IP lạ bị trần chung, IP quen của chủ trọ vẫn vào được.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class LoginLockoutTests(ApiFactory factory)
{
    private readonly HttpClient _client = factory.CreateClient();

    /// <summary>Đồng hồ test đứng yên ⇒ tiến 1 ms mỗi lần thử để thứ tự "sai sau lần đổi mật khẩu / lần đúng" rõ ràng như ngoài thực tế.</summary>
    private Task<HttpResponseMessage> LoginAsync(string phone, string password, string ip)
    {
        factory.Clock.Advance(TimeSpan.FromMilliseconds(1));
        return _client.PostJsonAsync("/api/v1/auth/login", new { username = phone, password }, clientIp: ip);
    }

    private async Task FailAsync(string phone, string ip, int times)
    {
        for (var i = 0; i < times; i++)
            (await LoginAsync(phone, $"doan-sai-{i}x", ip)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task FiveFailuresFromOneIp_BlockOnlyThatIp_OwnerFromAnotherIpLogsIn()
    {
        var owner = await _client.CreateActiveOwnerAsync();
        await FailAsync(owner.Phone, "203.0.113.10", 5);

        var attackerCorrect = await LoginAsync(owner.Phone, owner.Password, "203.0.113.10");
        attackerCorrect.StatusCode.Should().Be(HttpStatusCode.Unauthorized, "IP đã sai 5 lần ⇒ bị từ chối dù đúng mật khẩu");
        (await attackerCorrect.ReadProblemCodeAsync()).Should().Be("INVALID_CREDENTIALS");

        var ownerAtHome = await LoginAsync(owner.Phone, owner.Password, "198.51.100.20");
        ownerAtHome.StatusCode.Should().Be(HttpStatusCode.OK, await ownerAtHome.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task SuccessFromSameIp_ResetsCounter()
    {
        var owner = await _client.CreateActiveOwnerAsync();
        await FailAsync(owner.Phone, "203.0.113.11", 4);
        (await LoginAsync(owner.Phone, owner.Password, "203.0.113.11")).StatusCode.Should().Be(HttpStatusCode.OK);
        await FailAsync(owner.Phone, "203.0.113.11", 4);
        (await LoginAsync(owner.Phone, owner.Password, "203.0.113.11")).StatusCode.Should().Be(HttpStatusCode.OK, "đăng nhập đúng xóa bộ đếm của IP đó");
    }

    [Fact]
    public async Task PasswordReset_ClearsIpLock_TemporaryPasswordWorksFromSameIp()
    {
        var admin = await _client.LoginAdminAsync();
        var owner = await _client.CreateActiveOwnerAsync();
        await FailAsync(owner.Phone, "203.0.113.12", 5);

        var reset = await _client.PostJsonAsync($"/api/v1/admin/users/{owner.Organization.OwnerUserId}/reset-password", null, admin.AccessToken);
        var temporary = (await reset.ReadAsync<System.Text.Json.JsonElement>()).GetProperty("temporaryPassword").GetString()!;

        var login = await LoginAsync(owner.Phone, temporary, "203.0.113.12");
        login.StatusCode.Should().Be(HttpStatusCode.OK, "các lần đoán mật khẩu cũ không còn tính sau khi cấp lại");
    }

    [Fact]
    public async Task ManyUnknownIps_HitAccountCap_KnownIpStillLogsIn()
    {
        var owner = await _client.CreateActiveOwnerAsync();
        (await LoginAsync(owner.Phone, owner.Password, "198.51.100.30")).StatusCode.Should().Be(HttpStatusCode.OK, "IP quen của chủ trọ");

        for (var i = 1; i <= 30; i++)
            await FailAsync(owner.Phone, $"203.0.114.{i}", 1);

        (await LoginAsync(owner.Phone, owner.Password, "203.0.115.1")).StatusCode.Should().Be(HttpStatusCode.Unauthorized,
            "30 lần sai từ IP lạ trong 1 giờ ⇒ IP lạ bị từ chối tài khoản này");
        (await LoginAsync(owner.Phone, owner.Password, "198.51.100.30")).StatusCode.Should().Be(HttpStatusCode.OK, "IP quen không bị trần chặn");
    }
}
