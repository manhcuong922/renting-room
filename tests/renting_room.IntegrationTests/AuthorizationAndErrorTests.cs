using System.Net;
using System.Text;
using renting_room.IntegrationTests.Infrastructure;

namespace renting_room.IntegrationTests;

[Collection(ApiCollection.Name)]
public sealed class AuthorizationAndErrorTests(ApiFactory factory)
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task ProtectedEndpoint_Returns401_WithoutToken()
    {
        var response = await _client.GetAsync("/api/v1/rooms", accessToken: null);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        response.Headers.WwwAuthenticate.Should().ContainSingle(h => h.Scheme == "Bearer");
        (await response.ReadProblemCodeAsync()).Should().Be("AUTHENTICATION_REQUIRED");
    }

    [Fact]
    public async Task ProtectedEndpoint_Returns401_ForTamperedToken()
    {
        var admin = await _client.LoginAdminAsync();
        var tampered = admin.AccessToken[..^4] + "AAAA";

        var response = await _client.GetAsync("/api/v1/me", tampered);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await response.ReadProblemCodeAsync()).Should().Be("INVALID_TOKEN");
    }

    [Fact]
    public async Task OrgOwner_CannotCallAdminEndpoints()
    {
        var owner = await _client.CreateActiveOwnerAsync();

        var response = await _client.GetAsync("/api/v1/admin/organizations", owner.Tokens.AccessToken);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await response.ReadProblemCodeAsync()).Should().Be("FORBIDDEN");
    }

    [Fact]
    public async Task SystemAdmin_CannotCallBusinessEndpoints()
    {
        var admin = await _client.LoginAdminAsync();

        var response = await _client.GetAsync("/api/v1/rooms", admin.AccessToken);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task ValidationErrors_Return400_WithCamelCaseFieldNames()
    {
        var response = await _client.PostJsonAsync("/api/v1/auth/login", new { username = "", password = "" });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.ReadProblemCodeAsync()).Should().Be("VALIDATION_FAILED");
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("\"username\"").And.Contain("\"password\"").And.Contain("traceId");
    }

    [Fact]
    public async Task MalformedJson_Returns400_ProblemDetails()
    {
        var content = new StringContent("{ not json", Encoding.UTF8, "application/json");

        var response = await _client.PostAsync("/api/v1/auth/login", content);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.ReadProblemCodeAsync()).Should().Be("INVALID_REQUEST");
    }

    [Fact]
    public async Task Responses_IncludeSecurityHeaders()
    {
        var response = await _client.PostJsonAsync("/api/v1/auth/login", new { username = "x", password = "y" });

        response.Headers.GetValues("X-Content-Type-Options").Should().ContainSingle("nosniff");
        response.Headers.GetValues("X-Frame-Options").Should().ContainSingle("DENY");
        response.Headers.Contains("Server").Should().BeFalse();
    }

    [Fact]
    public async Task HealthEndpoints_AreAnonymous()
    {
        (await _client.GetAsync("/health/live", accessToken: null)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await _client.GetAsync("/health/ready", accessToken: null)).StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
