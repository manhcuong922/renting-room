using System.Net;
using renting_room.IntegrationTests.Infrastructure;

namespace renting_room.IntegrationTests;

/// <summary>L-08 (03/11/2026): số trang có trần — trang quá lớn làm tràn <c>(page - 1) * pageSize</c> ⇒ 400, không phải 500.</summary>
[Collection(ApiCollection.Name)]
public sealed class PagingTests(ApiFactory factory)
{
    private readonly HttpClient _client = factory.CreateClient();

    [Theory]
    [InlineData("/api/v1/properties")]
    [InlineData("/api/v1/rooms")]
    [InlineData("/api/v1/contracts")]
    [InlineData("/api/v1/invoices")]
    [InlineData("/api/v1/renters")]
    [InlineData("/api/v1/audit-logs")]
    [InlineData("/api/v1/org/login-events")]
    public async Task List_PageBeyondCap_Returns400(string url)
    {
        var token = (await _client.CreateActiveOwnerAsync()).Tokens.AccessToken;

        var response = await _client.GetAsync($"{url}?page=2147483647&pageSize=100", token);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest, await response.Content.ReadAsStringAsync());
        (await response.ReadProblemCodeAsync()).Should().Be("VALIDATION_FAILED");
    }
}
