using System.Net;
using System.Text.Json;
using renting_room.IntegrationTests.Infrastructure;

namespace renting_room.IntegrationTests;

/// <summary>CT-BR-44/45, CT-UC-21/22, PR-BR-16: người ký rời đi, hết hạn mà vẫn ở — phòng không tự về Trống, chủ trọ quyết định.</summary>
[Collection(ApiCollection.Name)]
public sealed class ContractAttentionTests(ApiFactory factory)
{
    private readonly HttpClient _client = factory.CreateClient();

    private async Task<JsonElement> ContractAsync(string token, Guid id) =>
        await (await _client.GetAsync($"/api/v1/contracts/{id}", token)).ReadAsync<JsonElement>();

    private static IEnumerable<string?> Flags(JsonElement e) => e.GetProperty("flags").EnumerateArray().Select(f => f.GetString());

    [Fact]
    public async Task RepresentativeMovesOut_RoomStaysOccupied_ResignForRemainingOccupant()
    {
        var owner = await _client.CreateActiveOwnerAsync();
        var token = owner.Tokens.AccessToken;
        var today = TestData.Today(factory);
        var propertyId = await _client.CreatePropertyAsync(token);
        var roomId = await _client.CreateRoomAsync(token, propertyId);
        var signer = await _client.CreateRenterAsync(token);
        var roommate = await _client.CreateRenterAsync(token, phone: "0912345678");
        var oldId = await _client.CreateContractAsync(token, roomId, signer, today.AddDays(-30), signer, roommate);
        (await _client.PostJsonAsync($"/api/v1/contracts/{oldId}/activate", null, token)).StatusCode.Should().Be(HttpStatusCode.NoContent);

        // Người ký chuyển đi hôm qua, bạn cùng phòng vẫn ở.
        var signerOccupancy = (await ContractAsync(token, oldId)).GetProperty("occupants").EnumerateArray()
            .Single(o => o.GetProperty("renterId").GetGuid() == signer).GetProperty("id").GetGuid();
        (await _client.PostJsonAsync($"/api/v1/contracts/{oldId}/occupants/{signerOccupancy}/end", new { moveOutDate = today.AddDays(-1) }, token))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);

        var detail = await ContractAsync(token, oldId);
        Flags(detail).Should().Equal("RepresentativeMovedOut");
        detail.GetProperty("warnings").EnumerateArray().Select(w => w.GetProperty("code").GetString()).Should().Contain("REPRESENTATIVE_MOVED_OUT");
        var room = await (await _client.GetAsync($"/api/v1/rooms/{roomId}", token)).ReadAsync<JsonElement>();
        room.GetProperty("status").GetString().Should().Be("Occupied", "còn người ở thì phòng vẫn đang thuê");
        Flags(room.GetProperty("currentContract")).Should().Equal("RepresentativeMovedOut");

        // Người đứng tên mới phải còn ở.
        (await (await _client.PostJsonAsync($"/api/v1/contracts/{oldId}/re-sign", new { handoverDate = today, representativeRenterId = signer }, token))
            .ReadProblemCodeAsync()).Should().Be("RESIGN_REPRESENTATIVE_NOT_OCCUPANT");

        var resigned = await _client.PostJsonAsync($"/api/v1/contracts/{oldId}/re-sign", new { handoverDate = today, representativeRenterId = roommate }, token);
        resigned.StatusCode.Should().Be(HttpStatusCode.Created, await resigned.Content.ReadAsStringAsync());
        var newId = await resigned.ReadIdAsync();

        var old = await ContractAsync(token, oldId);
        old.GetProperty("status").GetString().Should().Be("Liquidating");
        old.GetProperty("actualEndDate").GetString().Should().Be(today.ToString("yyyy-MM-dd"));
        var draft = await ContractAsync(token, newId);
        draft.GetProperty("status").GetString().Should().Be("Draft");
        draft.GetProperty("startDate").GetString().Should().Be(today.AddDays(1).ToString("yyyy-MM-dd"));
        draft.GetProperty("representativeRenterId").GetGuid().Should().Be(roommate);
        draft.GetProperty("previousContractId").GetGuid().Should().Be(oldId);
        draft.GetProperty("occupants").EnumerateArray().Select(o => o.GetProperty("renterId").GetGuid()).Should().Equal(roommate);
        draft.GetProperty("rentTerms")[0].GetProperty("monthlyRent").GetDecimal().Should().Be(3_500_000, "chép giá thuê hiện hành");

        (await _client.PostJsonAsync($"/api/v1/contracts/{newId}/activate", null, token)).StatusCode
            .Should().Be(HttpStatusCode.NoContent, "HĐ mới nối tiếp ngay sau ngày bàn giao, người ở không bị tính ở 2 nơi");
    }

    [Fact]
    public async Task ExpiredContract_RoomStaysOccupied_OwnerChoosesHoldover_ThenExtendClearsIt()
    {
        var owner = await _client.CreateActiveOwnerAsync();
        var token = owner.Tokens.AccessToken;
        var today = TestData.Today(factory);
        var (_, roomId, _, contractId) = await _client.CreateActiveContractAsync(token, today.AddYears(-1)); // hết hạn hôm qua

        (await _client.PostJsonAsync($"/api/v1/contracts/{contractId}/holdover", new { note = (string?)null }, token)).StatusCode
            .Should().Be(HttpStatusCode.NoContent);
        var detail = await ContractAsync(token, contractId);
        Flags(detail).Should().Equal("Holdover");
        detail.GetProperty("holdoverSince").GetString().Should().Be(today.ToString("yyyy-MM-dd"));
        detail.GetProperty("warnings").EnumerateArray().Select(w => w.GetProperty("code").GetString()).Should().Contain("HOLDOVER_SIGN_ADDENDUM");
        (await (await _client.PostJsonAsync($"/api/v1/contracts/{contractId}/holdover", new { note = "lần 2" }, token)).ReadProblemCodeAsync())
            .Should().Be("HOLDOVER_ALREADY");

        var room = await (await _client.GetAsync($"/api/v1/rooms/{roomId}", token)).ReadAsync<JsonElement>();
        room.GetProperty("status").GetString().Should().Be("Occupied", "hết hạn mà vẫn ở thì không báo phòng trống");
        Flags(room.GetProperty("currentContract")).Should().Equal("Holdover");

        (await _client.PostJsonAsync($"/api/v1/contracts/{contractId}/extend", new { newEndDate = today.AddMonths(6) }, token))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);
        detail = await ContractAsync(token, contractId);
        Flags(detail).Should().BeEmpty();
        detail.GetProperty("holdoverSince").ValueKind.Should().Be(JsonValueKind.Null);

        // Chưa hết hạn ⇒ không chọn "ở tiếp" được.
        (await (await _client.PostJsonAsync($"/api/v1/contracts/{contractId}/holdover", null, token)).ReadProblemCodeAsync())
            .Should().Be("CONTRACT_NOT_EXPIRED");
    }

    [Fact]
    public async Task ExpiredContract_AwaitsDecision_InContractList()
    {
        var owner = await _client.CreateActiveOwnerAsync();
        var token = owner.Tokens.AccessToken;
        var (propertyId, _, _, contractId) = await _client.CreateActiveContractAsync(token, TestData.Today(factory).AddYears(-1));

        var page = await (await _client.GetAsync($"/api/v1/contracts?propertyId={propertyId}", token)).ReadAsync<JsonElement>();
        var item = page.GetProperty("items").EnumerateArray().Single(i => i.GetProperty("id").GetGuid() == contractId);
        Flags(item).Should().Equal("ExpiredAwaitingDecision");
    }
}
