using System.Net;
using renting_room.IntegrationTests.Infrastructure;

namespace renting_room.IntegrationTests;

/// <summary>C-01: chủ trọ không bao giờ thấy hay dùng được dữ liệu của chủ trọ khác.</summary>
[Collection(ApiCollection.Name)]
public sealed class TenantIsolationTests(ApiFactory factory)
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task Owner_CannotReadOtherOrganizationsData()
    {
        var ownerA = await _client.CreateActiveOwnerAsync();
        var ownerB = await _client.CreateActiveOwnerAsync();
        var a = ownerA.Tokens.AccessToken;
        var b = ownerB.Tokens.AccessToken;

        var (propertyId, roomId, renterId, contractId) = await _client.CreateActiveContractAsync(a, TestData.Today(factory));

        foreach (var url in new[]
                 {
                     $"/api/v1/properties/{propertyId}", $"/api/v1/rooms/{roomId}", $"/api/v1/renters/{renterId}",
                     $"/api/v1/contracts/{contractId}"
                 })
        {
            (await _client.GetAsync(url, a)).StatusCode.Should().Be(HttpStatusCode.OK, url);
            (await _client.GetAsync(url, b)).StatusCode.Should().Be(HttpStatusCode.NotFound, url);
        }

        (await (await _client.GetAsync("/api/v1/rooms", b)).ReadAsync<Page<object>>()).TotalCount.Should().Be(0);
        (await (await _client.GetAsync("/api/v1/contracts", b)).ReadAsync<Page<object>>()).TotalCount.Should().Be(0);
    }

    [Fact]
    public async Task Owner_CannotLinkOtherOrganizationsRoomOrRenter()
    {
        var ownerA = await _client.CreateActiveOwnerAsync();
        var ownerB = await _client.CreateActiveOwnerAsync();
        var a = ownerA.Tokens.AccessToken;
        var b = ownerB.Tokens.AccessToken;

        var propertyA = await _client.CreatePropertyAsync(a);
        var roomA = await _client.CreateRoomAsync(a, propertyA);
        var renterA = await _client.CreateRenterAsync(a);

        var propertyB = await _client.CreatePropertyAsync(b);
        var roomB = await _client.CreateRoomAsync(b, propertyB);
        var renterB = await _client.CreateRenterAsync(b);

        // Phòng của A, người thuê của B (và ngược lại) ⇒ không tìm thấy, không tạo được hợp đồng chéo.
        var crossRoom = await _client.PostContractAsync(b, roomA, renterB, TestData.Today(factory));
        (await crossRoom.ReadProblemCodeAsync()).Should().Be("ROOM_NOT_FOUND");

        var crossRenter = await _client.PostContractAsync(b, roomB, renterA, TestData.Today(factory));
        (await crossRenter.ReadProblemCodeAsync()).Should().Be("RENTER_NOT_FOUND");

        var crossProperty = await _client.PostJsonAsync($"/api/v1/properties/{propertyA}/rooms", new { code = "X1", spec = new { maxOccupants = 2 } }, b);
        (await crossProperty.ReadProblemCodeAsync()).Should().Be("PROPERTY_NOT_FOUND");
    }

    [Fact]
    public async Task SameIdNumber_InDifferentOrganizations_DoesNotConflict()
    {
        var ownerA = await _client.CreateActiveOwnerAsync();
        var ownerB = await _client.CreateActiveOwnerAsync();
        var idNumber = TestData.NewCitizenId();

        await _client.CreateRenterAsync(ownerA.Tokens.AccessToken, idNumber);
        await _client.CreateRenterAsync(ownerB.Tokens.AccessToken, idNumber);

        var search = await _client.GetAsync($"/api/v1/renters?idNumber={idNumber}", ownerB.Tokens.AccessToken);
        (await search.ReadAsync<Page<object>>()).TotalCount.Should().Be(1);
    }
}
