using System.Net;
using renting_room.IntegrationTests.Infrastructure;

namespace renting_room.IntegrationTests;

/// <summary>C-01: chủ trọ không bao giờ thấy hay sửa được dữ liệu của chủ trọ khác.</summary>
[Collection(ApiCollection.Name)]
public sealed class TenantIsolationTests(ApiFactory factory)
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task Owner_CannotSeeRoomsOfAnotherOrganization()
    {
        var ownerA = await _client.CreateActiveOwnerAsync();
        var ownerB = await _client.CreateActiveOwnerAsync();

        var created = await _client.PostJsonAsync("/api/v1/rooms", new { name = "P101", monthlyRent = 3_500_000 }, ownerA.Tokens.AccessToken);
        created.StatusCode.Should().Be(HttpStatusCode.Created);
        var roomUrl = created.Headers.Location!.ToString();

        (await _client.GetAsync(roomUrl, ownerA.Tokens.AccessToken)).StatusCode.Should().Be(HttpStatusCode.OK);

        var crossTenant = await _client.GetAsync(roomUrl, ownerB.Tokens.AccessToken);
        crossTenant.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await crossTenant.ReadProblemCodeAsync()).Should().Be("ROOM_NOT_FOUND");

        var listB = await _client.GetAsync("/api/v1/rooms", ownerB.Tokens.AccessToken);
        (await listB.ReadAsync<List<object>>()).Should().BeEmpty();
    }
}
