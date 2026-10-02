using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace renting_room.IntegrationTests.Infrastructure;

public sealed record TokenResponse(
    string AccessToken,
    DateTimeOffset AccessTokenExpiresAt,
    string RefreshToken,
    DateTimeOffset RefreshTokenExpiresAt,
    bool MustChangePassword);

public sealed record CreatedOrganization(Guid OrganizationId, Guid OwnerUserId, string Username, string TemporaryPassword);

public sealed record OwnerAccount(CreatedOrganization Organization, string Phone, string Password, TokenResponse Tokens);

/// <summary>Các thao tác HTTP dùng lại giữa các test.</summary>
public static class TestApi
{
    public const string OwnerPassword = "ChuTro2026x";

    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    public static string NewPhone() => $"09{Random.Shared.Next(10_000_000, 99_999_999)}";

    public static string NewOrganizationCode() => $"T-{Guid.NewGuid():N}"[..12].ToUpperInvariant();

    /// <summary>Giá trị mặc định của tham số idempotencyKey: tự sinh key mới cho mỗi request (giống frontend chuẩn).</summary>
    public const string AutoKey = "<auto>";

    /// <param name="idempotencyKey"><see cref="AutoKey"/> = key mới; null = không gửi header; giá trị khác = gửi đúng giá trị đó.</param>
    /// <param name="clientIp">Giả lập IP client (dùng cho test rate limit theo IP).</param>
    public static Task<HttpResponseMessage> PostJsonAsync(
        this HttpClient client,
        string url,
        object? body,
        string? accessToken = null,
        string? idempotencyKey = AutoKey,
        string? clientIp = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = JsonContent.Create(body, options: Json) };
        if (accessToken is not null)
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        if (idempotencyKey is not null)
            request.Headers.Add("Idempotency-Key", idempotencyKey == AutoKey ? Guid.NewGuid().ToString() : idempotencyKey);
        if (clientIp is not null)
            request.Headers.Add(ApiFactory.TestClientIpHeader, clientIp);
        return client.SendAsync(request);
    }

    public static Task<HttpResponseMessage> GetAsync(this HttpClient client, string url, string? accessToken)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        if (accessToken is not null)
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return client.SendAsync(request);
    }

    public static async Task<T> ReadAsync<T>(this HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<T>(Json))!;

    /// <summary>Đọc trường <c>code</c> của ProblemDetails và kiểm tra content-type chuẩn RFC 9457.</summary>
    public static async Task<string?> ReadProblemCodeAsync(this HttpResponseMessage response)
    {
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.TryGetProperty("code", out var code) ? code.GetString() : null;
    }

    public static async Task<TokenResponse> LoginAsync(this HttpClient client, string username, string password)
    {
        var response = await client.PostJsonAsync("/api/v1/auth/login", new { username, password });
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return await response.ReadAsync<TokenResponse>();
    }

    public static Task<TokenResponse> LoginAdminAsync(this HttpClient client) =>
        client.LoginAsync(ApiFactory.AdminPhone, ApiFactory.AdminPassword);

    public static async Task<CreatedOrganization> CreateOrganizationAsync(this HttpClient client, string adminToken, string ownerPhone)
    {
        var response = await client.PostJsonAsync("/api/v1/admin/organizations", new
        {
            code = NewOrganizationCode(),
            name = "Nhà trọ kiểm thử",
            owner = new { fullName = "Chủ trọ kiểm thử", phone = ownerPhone }
        }, adminToken);

        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return await response.ReadAsync<CreatedOrganization>();
    }

    /// <summary>Tạo tổ chức mới + đăng nhập chủ trọ + đổi mật khẩu tạm → chủ trọ dùng được API nghiệp vụ.</summary>
    public static async Task<OwnerAccount> CreateActiveOwnerAsync(this HttpClient client)
    {
        var admin = await client.LoginAdminAsync();
        var phone = NewPhone();
        var organization = await client.CreateOrganizationAsync(admin.AccessToken, phone);

        var firstLogin = await client.LoginAsync(phone, organization.TemporaryPassword);
        var changed = await client.PostJsonAsync("/api/v1/auth/change-password",
            new { currentPassword = organization.TemporaryPassword, newPassword = OwnerPassword }, firstLogin.AccessToken);
        changed.StatusCode.Should().Be(HttpStatusCode.OK, await changed.Content.ReadAsStringAsync());

        return new OwnerAccount(organization, phone, OwnerPassword, await changed.ReadAsync<TokenResponse>());
    }
}
