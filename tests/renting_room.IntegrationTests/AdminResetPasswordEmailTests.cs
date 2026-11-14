using System.Net;
using System.Text.Json;
using renting_room.IntegrationTests.Infrastructure;

namespace renting_room.IntegrationTests;

/// <summary>
/// M-14 (11/11/2026, M01 ID-BR-11): SystemAdmin cấp lại mật khẩu cho tài khoản có email ⇒ mật khẩu tạm gửi tới email của chính chủ,
/// KHÔNG trả cho admin (trước đây admin nhận mật khẩu trong response ⇒ đăng nhập được làm chủ trọ). Tài khoản chưa có email: tạm giữ cách cũ.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class AdminResetPasswordEmailTests(ApiFactory factory) : IAsyncLifetime
{
    private const string OwnerEmail = "chu.tro.demo@example.com";
    private readonly HttpClient _client = factory.CreateClient();

    public Task InitializeAsync()
    {
        factory.Emails.Reset();
        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        factory.Emails.Reset();
        return Task.CompletedTask;
    }

    private async Task<(Guid UserId, string Phone, string Password)> CreateOwnerWithEmailAsync(string adminToken)
    {
        var phone = TestApi.NewPhone();
        var created = await _client.PostJsonAsync("/api/v1/admin/organizations", new
        {
            code = TestApi.NewOrganizationCode(),
            name = "Nhà trọ có email",
            owner = new { fullName = "Chủ trọ có email", phone, email = $"{Guid.NewGuid():N}@example.com" }
        }, adminToken);
        created.StatusCode.Should().Be(HttpStatusCode.Created);
        var organization = await created.ReadAsync<CreatedOrganization>();
        return (organization.OwnerUserId, phone, organization.TemporaryPassword);
    }

    [Fact]
    public async Task ResetForAccountWithEmail_SendsPasswordToOwnerEmail_AndNeverReturnsItToAdmin()
    {
        var admin = await _client.LoginAdminAsync();
        var (userId, phone, oldPassword) = await CreateOwnerWithEmailAsync(admin.AccessToken);

        var reset = await _client.PostJsonAsync($"/api/v1/admin/users/{userId}/reset-password", null, admin.AccessToken);

        reset.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await reset.ReadAsync<JsonElement>();
        body.GetProperty("temporaryPassword").ValueKind.Should().Be(JsonValueKind.Null, "admin không bao giờ thấy mật khẩu của chủ trọ");
        body.GetProperty("deliveredTo").GetString().Should().MatchRegex(@"^\w\*+@example\.com$", "chỉ hiện email đã che để admin biết thư đi đâu");
        body.GetProperty("expiresAt").ValueKind.Should().Be(JsonValueKind.String);

        var mail = factory.Emails.Sent.Should().ContainSingle().Subject;
        mail.To.Should().EndWith("@example.com");
        mail.Body.Should().Contain(phone, "thư nêu tên đăng nhập");
        var temporaryPassword = System.Text.RegularExpressions.Regex.Match(mail.Body, @"Mật khẩu tạm: (\S+)").Groups[1].Value;
        temporaryPassword.Should().HaveLength(12);

        (await _client.LoginAsync(phone, temporaryPassword)).MustChangePassword.Should().BeTrue("mật khẩu trong thư là mật khẩu tạm, bắt đổi");
        (await _client.PostJsonAsync("/api/v1/auth/login", new { username = phone, password = oldPassword }, idempotencyKey: null))
            .StatusCode.Should().Be(HttpStatusCode.Unauthorized, "mật khẩu cũ không còn dùng được");
    }

    [Fact]
    public async Task ResetForAccountWithoutEmail_KeepsCurrentBehavior_ReturnsPasswordToAdmin()
    {
        var admin = await _client.LoginAdminAsync();
        var owner = await _client.CreateActiveOwnerAsync(); // chủ trọ chỉ có SĐT

        var reset = await _client.PostJsonAsync($"/api/v1/admin/users/{owner.Organization.OwnerUserId}/reset-password", null, admin.AccessToken);

        reset.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await reset.ReadAsync<JsonElement>();
        body.GetProperty("temporaryPassword").GetString().Should().HaveLength(12);
        body.GetProperty("deliveredTo").ValueKind.Should().Be(JsonValueKind.Null);
        factory.Emails.Sent.Should().BeEmpty();
    }

    [Theory]
    [InlineData(false, "EMAIL_NOT_CONFIGURED")]
    [InlineData(true, "EMAIL_SEND_FAILED")]
    public async Task EmailCannotBeDelivered_ResetIsCancelled_OldPasswordStillWorks(bool smtpConfiguredButRejects, string expectedCode)
    {
        var admin = await _client.LoginAdminAsync();
        var (userId, phone, oldPassword) = await CreateOwnerWithEmailAsync(admin.AccessToken);
        factory.Emails.IsConfigured = smtpConfiguredButRejects;
        factory.Emails.FailSending = smtpConfiguredButRejects;

        var reset = await _client.PostJsonAsync($"/api/v1/admin/users/{userId}/reset-password", null, admin.AccessToken);

        reset.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        (await reset.ReadProblemCodeAsync()).Should().Be(expectedCode);
        (await _client.LoginAsync(phone, oldPassword)).AccessToken.Should().NotBeNullOrEmpty("không đổi mật khẩu khi thư không tới — tránh khóa chủ trọ ngoài hệ thống");
    }
}
