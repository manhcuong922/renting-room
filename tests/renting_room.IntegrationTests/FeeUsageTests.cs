using System.Net;
using System.Text.Json;
using renting_room.IntegrationTests.Infrastructure;

namespace renting_room.IntegrationTests;

/// <summary>FE-UC-08: phòng đang dùng dịch vụ — thêm / bớt hàng loạt, kết quả từng HĐ.</summary>
[Collection(ApiCollection.Name)]
public sealed class FeeUsageTests(ApiFactory factory)
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task AddServiceToManyRooms_ThenRemoveOne_ReportsEachContract()
    {
        var owner = await _client.CreateActiveOwnerAsync();
        var token = owner.Tokens.AccessToken;
        var today = TestData.Today(factory);
        var propertyId = await _client.CreatePropertyAsync(token);
        var contracts = new List<Guid>();
        foreach (var code in new[] { "201", "202" })
        {
            var roomId = await _client.CreateRoomAsync(token, propertyId, code);
            var contractId = await _client.CreateContractAsync(token, roomId, await _client.CreateRenterAsync(token), today);
            (await _client.PostJsonAsync($"/api/v1/contracts/{contractId}/activate", null, token)).StatusCode.Should().Be(HttpStatusCode.NoContent);
            contracts.Add(contractId);
        }
        await _client.CreateRoomAsync(token, propertyId, "203"); // phòng trống
        var airCon = await (await _client.PostJsonAsync($"/api/v1/properties/{propertyId}/fee-types", new
        {
            name = "Phí điều hòa", group = "Service", chargeBasis = "PerRoom", unit = "phòng", autoAttach = false,
            initialPrice = new { effectiveFrom = today.AddDays(-30), unitPrice = 150000 }
        }, token)).ReadIdAsync();

        var before = await (await _client.GetAsync($"/api/v1/fee-types/{airCon}/usage", token)).ReadAsync<List<JsonElement>>();
        before.Should().HaveCount(3).And.OnlyContain(r => !r.GetProperty("isUsing").GetBoolean());
        before.Single(r => r.GetProperty("roomCode").GetString() == "203").GetProperty("contractId").ValueKind.Should().Be(JsonValueKind.Null);

        var added = await (await _client.PostJsonAsync($"/api/v1/fee-types/{airCon}/usage",
            new { action = "Add", contractIds = contracts.Append(Guid.NewGuid()).ToArray() }, token)).ReadAsync<List<JsonElement>>();
        added.Count(r => r.GetProperty("succeeded").GetBoolean()).Should().Be(2);
        added.Single(r => !r.GetProperty("succeeded").GetBoolean()).GetProperty("errorCode").GetString().Should().Be("CONTRACT_NOT_FOUND");

        (await _client.PostJsonAsync($"/api/v1/fee-types/{airCon}/usage", new { action = "Remove", contractIds = new[] { contracts[1] } }, token))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        var after = await (await _client.GetAsync($"/api/v1/fee-types/{airCon}/usage", token)).ReadAsync<List<JsonElement>>();
        after.Single(r => r.GetProperty("roomCode").GetString() == "201").GetProperty("isUsing").GetBoolean().Should().BeTrue();
        after.Single(r => r.GetProperty("roomCode").GetString() == "202").GetProperty("isUsing").GetBoolean().Should().BeFalse();
    }
}
