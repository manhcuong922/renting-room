using System.Net;
using System.Text.Json;
using renting_room.IntegrationTests.Infrastructure;

namespace renting_room.IntegrationTests;

[Collection(ApiCollection.Name)]
public sealed class ContractDataReviewTests(ApiFactory factory)
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task Review_FindsRelationshipBrokenByProfileEdit_AndFeesWithoutPrice()
    {
        var owner = await _client.CreateActiveOwnerAsync();
        var token = owner.Tokens.AccessToken;
        var today = TestData.Today(factory);
        var propertyId = await _client.CreatePropertyAsync(token);
        var father = await (await _client.PostJsonAsync("/api/v1/renters", new
        {
            fullName = "Bố", dateOfBirth = "1985-05-01", gender = "Male", phone = "0912345678", idType = "CitizenId", idNumber = TestData.NewCitizenId()
        }, token)).ReadIdAsync();
        var child = await (await _client.PostJsonAsync("/api/v1/renters", new
        {
            fullName = "Con", dateOfBirth = "2016-01-01", gender = "Male", idType = "CitizenId", idNumber = TestData.NewCitizenId()
        }, token)).ReadIdAsync();
        var roomId = await _client.CreateRoomAsync(token, propertyId);
        var contractId = await (await _client.PostJsonAsync("/api/v1/contracts", new
        {
            roomId,
            contract = new
            {
                representativeRenterId = father, startDate = today, monthlyRent = 3_000_000,
                occupants = new object[] { new { renterId = father }, new { renterId = child, relationshipType = "Child" } }
            }
        }, token)).ReadIdAsync();
        (await _client.PostJsonAsync($"/api/v1/contracts/{contractId}/activate", null, token)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        await _client.InstallMeterAsync(token, roomId, await _client.FeeIdAsync(token, propertyId, "Điện"), today);
        await _client.InstallMeterAsync(token, roomId, await _client.FeeIdAsync(token, propertyId, "Nước"), today);

        // Sửa nhầm ngày sinh của con thành lớn tuổi hơn bố (sau khi đã ký).
        var profile = await (await _client.GetAsync($"/api/v1/renters/{child}", token)).ReadAsync<JsonElement>();
        (await _client.PutJsonAsync($"/api/v1/renters/{child}", new
        {
            renter = new { fullName = "Con", dateOfBirth = "1980-01-01", gender = "Male", idType = "CitizenId" },
            version = profile.GetProperty("version").GetString()
        }, token)).StatusCode.Should().Be(HttpStatusCode.OK);

        var issues = await (await _client.GetAsync($"/api/v1/contracts/data-review?propertyId={propertyId}", token)).ReadAsync<List<JsonElement>>();

        issues.Should().Contain(i => i.GetProperty("code").GetString() == "RELATIONSHIP_AGE_MISMATCH" && i.GetProperty("renterName").GetString() == "Con");
        issues.Where(i => i.GetProperty("code").GetString() == "FEE_PRICE_MISSING")
            .Select(i => i.GetProperty("message").GetString()).Should().HaveCount(2, "phòng có công tơ điện, nước nhưng khu chưa nhập giá");
        issues.Should().OnlyContain(i => i.GetProperty("contractId").GetGuid() == contractId);
    }

    [Fact]
    public async Task Review_IsEmpty_ForCleanData()
    {
        var owner = await _client.CreateActiveOwnerAsync();
        var token = owner.Tokens.AccessToken;
        var (propertyId, _, _, contractId) = await _client.CreateActiveContractAsync(token, TestData.Today(factory));
        foreach (var fee in await (await _client.GetAsync($"/api/v1/properties/{propertyId}/fee-types", token)).ReadAsync<List<JsonElement>>())
            await _client.PostJsonAsync($"/api/v1/fee-types/{fee.GetProperty("id").GetGuid()}/prices",
                new { effectiveFrom = TestData.Today(factory), unitPrice = 3000 }, token);
        contractId.Should().NotBeEmpty();

        (await (await _client.GetAsync($"/api/v1/contracts/data-review?propertyId={propertyId}", token)).ReadAsync<List<JsonElement>>())
            .Should().BeEmpty();
    }
}
