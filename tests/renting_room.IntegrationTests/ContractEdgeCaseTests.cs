using System.Net;
using System.Text.Json;
using renting_room.IntegrationTests.Infrastructure;

namespace renting_room.IntegrationTests;

/// <summary>Các ca biên tìm được khi rà nghiệp vụ hợp đồng lần 2 (99-self-review B5).</summary>
[Collection(ApiCollection.Name)]
public sealed class ContractEdgeCaseTests(ApiFactory factory)
{
    private readonly HttpClient _client = factory.CreateClient();

    private async Task<Guid> RenterAsync(string token, string fullName, string dateOfBirth, string gender) =>
        await (await _client.PostJsonAsync("/api/v1/renters", new
        {
            fullName, dateOfBirth, gender, phone = "0912345678", idType = "CitizenId", idNumber = TestData.NewCitizenId()
        }, token)).ReadIdAsync();

    private async Task<Guid> OccupantIdAsync(string token, Guid contractId, Guid renterId)
    {
        var detail = await (await _client.GetAsync($"/api/v1/contracts/{contractId}", token)).ReadAsync<JsonElement>();
        return detail.GetProperty("occupants").EnumerateArray()
            .Single(o => o.GetProperty("renterId").GetGuid() == renterId).GetProperty("id").GetGuid();
    }

    [Fact]
    public async Task FamilyLargerThanRoomType_IsActivated_ChildUnder14NeedsNoIdDocument()
    {
        var owner = await _client.CreateActiveOwnerAsync();
        var token = owner.Tokens.AccessToken;
        var roomId = await _client.CreateRoomAsync(token, await _client.CreatePropertyAsync(token), maxOccupants: 2);
        var father = await RenterAsync(token, "Bố", "1985-05-01", "Male");
        var mother = await RenterAsync(token, "Mẹ", "1988-03-02", "Female");
        // RT-BR-01: dưới 14 tuổi được để trống giấy tờ; từ 14 tuổi bắt buộc.
        (await _client.PostJsonAsync("/api/v1/renters", new { fullName = "Con lớn", dateOfBirth = TestData.Today(factory).AddYears(-15), gender = "Male" }, token))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var baby = await (await _client.PostJsonAsync("/api/v1/renters", new { fullName = "Con", dateOfBirth = "2025-01-01", gender = "Female" }, token))
            .ReadIdAsync();
        var contractId = await (await _client.PostJsonAsync("/api/v1/contracts", new
        {
            roomId,
            contract = new
            {
                representativeRenterId = father, startDate = TestData.Today(factory), monthlyRent = 3_000_000,
                occupants = new object[] { new { renterId = father }, new { renterId = mother, relationshipType = "Wife" }, new { renterId = baby, relationshipType = "Child" } }
            }
        }, token)).ReadIdAsync();

        (await _client.PostJsonAsync($"/api/v1/contracts/{contractId}/activate", null, token))
            .StatusCode.Should().Be(HttpStatusCode.NoContent, "phòng loại 2 người vẫn cho gia đình 3 người ở (PR-BR-06)");

        // Người không có giấy tờ không được đứng tên HĐ.
        (await (await _client.PostJsonAsync("/api/v1/contracts", new
        {
            roomId = await _client.CreateRoomAsync(token, await _client.CreatePropertyAsync(token)),
            contract = new { representativeRenterId = baby, startDate = TestData.Today(factory), monthlyRent = 3_000_000, occupants = new[] { new { renterId = baby } } }
        }, token)).ReadProblemCodeAsync()).Should().Be("REPRESENTATIVE_ID_REQUIRED");
    }

    [Fact]
    public async Task MovedOutOccupant_CannotBeMovedOutAgain()
    {
        var owner = await _client.CreateActiveOwnerAsync();
        var token = owner.Tokens.AccessToken;
        var today = TestData.Today(factory);
        var (_, _, renterId, contractId) = await _client.CreateActiveContractAsync(token, today.AddDays(-10));
        var occupantId = await OccupantIdAsync(token, contractId, renterId);
        var end = $"/api/v1/contracts/{contractId}/occupants/{occupantId}/end";

        (await _client.PostJsonAsync(end, new { moveOutDate = today.AddDays(-2) }, token)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await (await _client.PostJsonAsync(end, new { moveOutDate = today.AddDays(30) }, token))
            .ReadProblemCodeAsync()).Should().Be("OCCUPANT_ALREADY_MOVED_OUT");
    }

    [Fact]
    public async Task CancelLiquidation_IsBlocked_WhenOccupantAlreadyLivesInAnotherRoom()
    {
        var owner = await _client.CreateActiveOwnerAsync();
        var token = owner.Tokens.AccessToken;
        var today = TestData.Today(factory);
        var (_, _, mover, oldContract) = await _client.CreateActiveContractAsync(token, today.AddDays(-30));
        await _client.PostJsonAsync($"/api/v1/contracts/{oldContract}/liquidation/start", new { actualEndDate = today, reason = "RoomTransfer" }, token);

        var (_, _, _, newContract) = await _client.CreateActiveContractAsync(token, today);
        (await _client.PostJsonAsync($"/api/v1/contracts/{newContract}/occupants",
            new { renterId = mover, moveInDate = today, relationshipType = "CoTenant" }, token)).StatusCode.Should().Be(HttpStatusCode.NoContent);

        var cancel = await _client.PostJsonAsync($"/api/v1/contracts/{oldContract}/liquidation/cancel", null, token);
        (await cancel.ReadProblemCodeAsync()).Should().Be("OCCUPANT_LIVES_ELSEWHERE");
    }

    [Fact]
    public async Task RoomCannotBeArchivedOrMaintained_OnTheMoveOutDay()
    {
        var owner = await _client.CreateActiveOwnerAsync();
        var token = owner.Tokens.AccessToken;
        var today = TestData.Today(factory);
        var start = TestData.StartWithinOnePeriod(today, 20);
        var (_, roomId, _, contractId) = await _client.CreateActiveContractAsync(token, start, anchorDay: start.Day);
        await _client.PostJsonAsync($"/api/v1/contracts/{contractId}/liquidation/start", new { actualEndDate = today, reason = "MutualAgreement" }, token);
        await _client.SettleAndCompleteAsync(token, contractId);

        (await (await _client.PostJsonAsync($"/api/v1/rooms/{roomId}/archive", null, token)).ReadProblemCodeAsync())
            .Should().Be("ROOM_HAS_CONTRACTS");
        (await (await _client.PostJsonAsync($"/api/v1/rooms/{roomId}/maintenance/start", null, token)).ReadProblemCodeAsync())
            .Should().Be("ROOM_OCCUPIED");
    }

    [Fact]
    public async Task Deposit_AboveTwelveMonthsRent_IsRejected()
    {
        var owner = await _client.CreateActiveOwnerAsync();
        var token = owner.Tokens.AccessToken;
        var roomId = await _client.CreateRoomAsync(token, await _client.CreatePropertyAsync(token));
        var renterId = await _client.CreateRenterAsync(token);

        var response = await _client.PostJsonAsync("/api/v1/contracts", new
        {
            roomId,
            contract = new { representativeRenterId = renterId, startDate = TestData.Today(factory), monthlyRent = 1_000_000, depositAmount = 100_000_000 }
        }, token);

        (await response.ReadProblemCodeAsync()).Should().Be("DEPOSIT_TOO_HIGH");
    }

    [Fact]
    public async Task MovingDraftStartLater_KeepsVehicleRegistrationInsideContract()
    {
        var owner = await _client.CreateActiveOwnerAsync();
        var token = owner.Tokens.AccessToken;
        var today = TestData.Today(factory);
        var roomId = await _client.CreateRoomAsync(token, await _client.CreatePropertyAsync(token));
        var renterId = await _client.CreateRenterAsync(token);
        var contractId = await _client.CreateContractAsync(token, roomId, renterId, today);
        await _client.PostJsonAsync($"/api/v1/contracts/{contractId}/vehicles", new { vehicleType = "Bicycle" }, token);

        var draft = await (await _client.GetAsync($"/api/v1/contracts/{contractId}", token)).ReadAsync<JsonElement>();
        var later = today.AddDays(1);
        (await _client.PutJsonAsync($"/api/v1/contracts/{contractId}", new
        {
            contract = new { representativeRenterId = renterId, startDate = later, monthlyRent = 3_500_000, occupants = new[] { new { renterId } } },
            version = draft.GetProperty("version").GetString()
        }, token)).StatusCode.Should().Be(HttpStatusCode.NoContent);

        var updated = await (await _client.GetAsync($"/api/v1/contracts/{contractId}", token)).ReadAsync<JsonElement>();
        DateOnly.Parse(updated.GetProperty("vehicles")[0].GetProperty("registeredFrom").GetString()!).Should().Be(later);
    }
}
