using System.Net;
using System.Text;
using System.Text.Json;
using renting_room.IntegrationTests.Infrastructure;

namespace renting_room.IntegrationTests;

[Collection(ApiCollection.Name)]
public sealed class AuthorizationAndErrorTests(ApiFactory factory)
{
    private readonly HttpClient _client = factory.CreateClient();

    private static async Task<IReadOnlyList<string>> ReadValidationKeysAsync(HttpResponseMessage response)
    {
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.GetProperty("errors").EnumerateObject().Select(p => p.Name).ToList();
    }

    [Fact]
    public async Task ValidationErrorKeys_MatchRequestBodyPaths()
    {
        var owner = await _client.CreateActiveOwnerAsync();
        var token = owner.Tokens.AccessToken;
        var today = TestData.Today(factory);
        var roomId = await _client.CreateRoomAsync(token, await _client.CreatePropertyAsync(token));
        var contractId = await _client.CreateContractAsync(token, roomId, await _client.CreateRenterAsync(token), today);

        // Body phẳng ⇒ key không có tiền tố.
        var renter = await _client.PostJsonAsync("/api/v1/renters",
            new { fullName = "", dateOfBirth = "2000-01-01", gender = "Male", idType = "CitizenId", idNumber = "123" }, token);
        (await ReadValidationKeysAsync(renter)).Should().BeEquivalentTo("fullName", "idNumber");

        var asset = await _client.PostJsonAsync($"/api/v1/contracts/{contractId}/assets", new { name = "", quantity = 0 }, token);
        (await ReadValidationKeysAsync(asset)).Should().BeEquivalentTo("name", "quantity");

        // Body lồng ⇒ key theo đường dẫn JSON.
        var room = await _client.PostJsonAsync($"/api/v1/properties/{Guid.NewGuid()}/rooms",
            new { code = "R1", spec = new { maxOccupants = 0 } }, token);
        (await ReadValidationKeysAsync(room)).Should().BeEquivalentTo("spec.maxOccupants");
    }

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
