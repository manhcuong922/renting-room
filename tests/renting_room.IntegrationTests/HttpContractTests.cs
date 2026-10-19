using System.Net;
using System.Net.Http.Headers;
using System.Text;
using renting_room.IntegrationTests.Infrastructure;

namespace renting_room.IntegrationTests;

/// <summary>
/// Hành vi HTTP chung của mọi endpoint (lỗi định dạng, body bắt buộc / tùy chọn, header không cache) — phải giữ nguyên
/// dù tầng API viết bằng Minimal API hay controller.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class HttpContractTests(ApiFactory factory)
{
    private readonly HttpClient _client = factory.CreateClient();

    private Task<HttpResponseMessage> SendAsync(HttpMethod method, string url, HttpContent? content, string? token = null)
    {
        var request = new HttpRequestMessage(method, url) { Content = content };
        if (token is not null)
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return _client.SendAsync(request);
    }

    private static async Task ShouldBeInvalidRequestAsync(HttpResponseMessage response)
    {
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.ReadProblemCodeAsync()).Should().Be("INVALID_REQUEST");
    }

    [Fact]
    public async Task RequiredBody_Missing_Returns400_InvalidRequest()
    {
        var response = await SendAsync(HttpMethod.Post, "/api/v1/auth/login", new StringContent("", Encoding.UTF8, "application/json"));

        await ShouldBeInvalidRequestAsync(response);
    }

    [Fact]
    public async Task RequiredBody_JsonNull_Returns400_InvalidRequest()
    {
        var response = await SendAsync(HttpMethod.Post, "/api/v1/auth/login", new StringContent("null", Encoding.UTF8, "application/json"));

        await ShouldBeInvalidRequestAsync(response);
    }

    [Fact]
    public async Task WrongContentType_Returns415()
    {
        var owner = await _client.CreateActiveOwnerAsync();

        var response = await SendAsync(HttpMethod.Post, "/api/v1/properties", new StringContent("name=x", Encoding.UTF8, "text/plain"),
            owner.Tokens.AccessToken);

        response.StatusCode.Should().Be(HttpStatusCode.UnsupportedMediaType);
        (await response.ReadProblemCodeAsync()).Should().Be("UNSUPPORTED_MEDIA_TYPE");
    }

    [Fact]
    public async Task MissingRequiredQuery_Returns400_InvalidRequest()
    {
        var owner = await _client.CreateActiveOwnerAsync();
        var token = owner.Tokens.AccessToken;
        var propertyId = await _client.CreatePropertyAsync(token);

        await ShouldBeInvalidRequestAsync(await _client.GetAsync($"/api/v1/properties/{propertyId}/billing/preview", token));
        await ShouldBeInvalidRequestAsync(await _client.GetAsync($"/api/v1/properties/{propertyId}/meter-reading-sheet", token));
    }

    [Fact]
    public async Task InvalidEnumInQuery_Returns400_InvalidRequest()
    {
        var owner = await _client.CreateActiveOwnerAsync();

        await ShouldBeInvalidRequestAsync(await _client.GetAsync("/api/v1/contracts?status=NotAStatus", owner.Tokens.AccessToken));
    }

    [Fact]
    public async Task OptionalBody_CanBeOmitted()
    {
        var owner = await _client.CreateActiveOwnerAsync();
        var token = owner.Tokens.AccessToken;
        var roomId = await _client.CreateRoomAsync(token, await _client.CreatePropertyAsync(token));

        var response = await SendAsync(HttpMethod.Post, $"/api/v1/rooms/{roomId}/maintenance/start", null, token);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task SensitiveEndpoints_AreNotCached_OthersAreUnaffected()
    {
        var login = await _client.PostJsonAsync("/api/v1/auth/login", new { username = "0900000000", password = "wrong-password" });
        login.Headers.CacheControl?.NoStore.Should().BeTrue();
        login.Headers.Pragma.Should().ContainSingle(p => p.Name == "no-cache");

        var owner = await _client.CreateActiveOwnerAsync();
        var me = await _client.GetAsync("/api/v1/me", owner.Tokens.AccessToken);
        me.StatusCode.Should().Be(HttpStatusCode.OK);
        (me.Headers.CacheControl?.NoStore ?? false).Should().BeFalse();
    }
}
