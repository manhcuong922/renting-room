using System.Net;
using System.Text.Json;
using renting_room.IntegrationTests.Infrastructure;

namespace renting_room.IntegrationTests;

/// <summary>M05 CT-BR-49..52: đồng thuê, người đứng tên không là người ở thường của HĐ khác, đổi người đứng tên trên cùng HĐ, chuyển người.</summary>
[Collection(ApiCollection.Name)]
public sealed class ContractPartiesTests(ApiFactory factory)
{
    private readonly HttpClient _client = factory.CreateClient();

    private static object Occupant(Guid renterId, string? role = null, string? relationshipType = null, bool? guardianConsent = null) =>
        new { renterId, role, relationshipType, guardianConsent };

    private Task<HttpResponseMessage> PostContractAsync(string token, Guid roomId, Guid representative, DateOnly start, params object[] occupants) =>
        _client.PostJsonAsync("/api/v1/contracts", new
        {
            roomId, contract = new { representativeRenterId = representative, startDate = start, monthlyRent = 3_500_000, depositAmount = 0, occupants }
        }, token);

    private async Task<Guid> ActiveContractAsync(string token, Guid roomId, Guid representative, DateOnly start, params object[] occupants)
    {
        var id = await (await PostContractAsync(token, roomId, representative, start, occupants)).ReadIdAsync();
        var activated = await _client.PostJsonAsync($"/api/v1/contracts/{id}/activate", null, token);
        activated.StatusCode.Should().Be(HttpStatusCode.NoContent, await activated.Content.ReadAsStringAsync());
        return id;
    }

    private async Task<JsonElement> DetailAsync(string token, Guid contractId) =>
        await (await _client.GetAsync($"/api/v1/contracts/{contractId}", token)).ReadAsync<JsonElement>();

    private static JsonElement OccupantOf(JsonElement detail, Guid renterId) =>
        detail.GetProperty("occupants").EnumerateArray().Single(o => o.GetProperty("renterId").GetGuid() == renterId);

    /// <summary>Trẻ nhỏ — hồ sơ không có giấy tờ.</summary>
    private async Task<Guid> RenterWithoutIdAsync(string token) =>
        await (await _client.PostJsonAsync("/api/v1/renters", new
        {
            fullName = "Bé Na", dateOfBirth = TestData.Today(factory).AddYears(-7), gender = "Female", nationality = "VN"
        }, token)).ReadIdAsync();

    [Fact]
    public async Task CoTenant_IsAPaperRole_RequiresIdNumber_AndCanBeChanged()
    {
        var owner = await _client.CreateActiveOwnerAsync();
        var token = owner.Tokens.AccessToken;
        var today = TestData.Today(factory);
        var propertyId = await _client.CreatePropertyAsync(token);
        var representative = await _client.CreateRenterAsync(token);
        var friend = await _client.CreateRenterAsync(token);
        var child = await RenterWithoutIdAsync(token);

        var rejected = await PostContractAsync(token, await _client.CreateRoomAsync(token, propertyId), representative, today,
            Occupant(representative), Occupant(child, "CoTenant", "Child", true));
        (await rejected.ReadProblemCodeAsync()).Should().Be("CO_TENANT_ID_REQUIRED");

        var contractId = await ActiveContractAsync(token, await _client.CreateRoomAsync(token, propertyId), representative, today,
            Occupant(representative, "CoTenant"), Occupant(friend, "CoTenant", "CoTenant"));
        var detail = await DetailAsync(token, contractId);
        OccupantOf(detail, friend).GetProperty("role").GetString().Should().Be("CoTenant");
        OccupantOf(detail, representative).GetProperty("role").GetString().Should().Be("Resident", "người đứng tên không mang vai trò đồng thuê");

        var occupantsUrl = $"/api/v1/contracts/{contractId}/occupants";
        (await (await _client.PostJsonAsync(occupantsUrl, new { renterId = child, moveInDate = today, relationshipType = "Child", guardianConsent = true, role = "CoTenant" }, token))
            .ReadProblemCodeAsync()).Should().Be("CO_TENANT_ID_REQUIRED");
        (await _client.PostJsonAsync(occupantsUrl, new { renterId = child, moveInDate = today, relationshipType = "Child", guardianConsent = true }, token))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);

        var friendOccupancy = OccupantOf(detail, friend).GetProperty("id").GetGuid();
        (await _client.PutJsonAsync($"{occupantsUrl}/{friendOccupancy}/role", new { role = "Resident" }, token)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        OccupantOf(await DetailAsync(token, contractId), friend).GetProperty("role").GetString().Should().Be("Resident");
        var own = OccupantOf(detail, representative).GetProperty("id").GetGuid();
        (await (await _client.PutJsonAsync($"{occupantsUrl}/{own}/role", new { role = "CoTenant" }, token)).ReadProblemCodeAsync())
            .Should().Be("OCCUPANT_IS_REPRESENTATIVE");
    }

    [Fact]
    public async Task Representative_CannotBeAPlainResidentOfAnotherContract_ButCanBeCoTenant()
    {
        var owner = await _client.CreateActiveOwnerAsync();
        var token = owner.Tokens.AccessToken;
        var today = TestData.Today(factory);
        var propertyId = await _client.CreatePropertyAsync(token);
        var host = await _client.CreateRenterAsync(token);
        var resident = await _client.CreateRenterAsync(token);
        var hostContract = await ActiveContractAsync(token, await _client.CreateRoomAsync(token, propertyId), host, today,
            Occupant(host), Occupant(resident, relationshipType: "CoTenant"));

        // Người ở thường của phòng 1 đứng tên phòng 2 (cho người khác ở) ⇒ chặn khi kích hoạt; đổi thành đồng thuê ở phòng 1 thì được.
        var second = await (await PostContractAsync(token, await _client.CreateRoomAsync(token, propertyId), resident, today,
            Occupant(await _client.CreateRenterAsync(token), relationshipType: "CoTenant"))).ReadIdAsync();
        (await DetailAsync(token, second)).GetProperty("warnings").EnumerateArray().Select(w => w.GetProperty("code").GetString())
            .Should().Contain("REPRESENTATIVE_IS_RESIDENT_ELSEWHERE", "HĐ nháp chỉ cảnh báo — kích hoạt mới chặn");
        (await (await _client.PostJsonAsync($"/api/v1/contracts/{second}/activate", null, token)).ReadProblemCodeAsync())
            .Should().Be("REPRESENTATIVE_IS_RESIDENT_ELSEWHERE");
        var residentOccupancy = OccupantOf(await DetailAsync(token, hostContract), resident).GetProperty("id").GetGuid();
        (await _client.PutJsonAsync($"/api/v1/contracts/{hostContract}/occupants/{residentOccupancy}/role", new { role = "CoTenant" }, token))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await _client.PostJsonAsync($"/api/v1/contracts/{second}/activate", null, token)).StatusCode.Should().Be(HttpStatusCode.NoContent);

        // Chiều ngược lại: người đang đứng tên phòng 3 (không ở đó) vào phòng 1 làm người ở thường ⇒ chặn; làm đồng thuê thì được.
        var landlordOfThird = await _client.CreateRenterAsync(token);
        await ActiveContractAsync(token, await _client.CreateRoomAsync(token, propertyId), landlordOfThird, today,
            Occupant(await _client.CreateRenterAsync(token), relationshipType: "CoTenant"));
        var add = $"/api/v1/contracts/{hostContract}/occupants";
        (await (await _client.PostJsonAsync(add, new { renterId = landlordOfThird, moveInDate = today, relationshipType = "CoTenant" }, token))
            .ReadProblemCodeAsync()).Should().Be("REPRESENTATIVE_IS_RESIDENT_ELSEWHERE");
        (await _client.PostJsonAsync(add, new { renterId = landlordOfThird, moveInDate = today, relationshipType = "CoTenant", role = "CoTenant" }, token))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task ChangeRepresentative_KeepsContractDebtAndDeposit_FlagsMissingDocument_ClearsRelationships()
    {
        var owner = await _client.CreateActiveOwnerAsync();
        var token = owner.Tokens.AccessToken;
        var today = TestData.Today(factory);
        var month = new DateOnly(today.Year, today.Month, 1).AddMonths(-1);
        var propertyId = await _client.CreatePropertyAsync(token, anchorDay: 1, chargeMode: "Postpaid");
        var roomId = await _client.CreateRoomAsync(token, propertyId);
        var oldRepresentative = await _client.CreateRenterAsync(token);
        var successor = await _client.CreateRenterAsync(token);
        var contractId = await ActiveContractAsync(token, roomId, oldRepresentative, month,
            Occupant(oldRepresentative), Occupant(successor, relationshipType: "CoTenant"));
        await _client.PutJsonAsync($"/api/v1/contracts/{contractId}/signed-document", new { hasSignedDocument = true, note = "Tủ hồ sơ" }, token);
        await _client.PostJsonAsync("/api/v1/invoices/generate", new { propertyId, billingMonth = $"{month:yyyy-MM}", roomIds = new[] { roomId } }, token);
        var page = await (await _client.GetAsync($"/api/v1/invoices?contractId={contractId}", token)).ReadAsync<JsonElement>();
        (await _client.PostJsonAsync($"/api/v1/invoices/{page.GetProperty("items")[0].GetProperty("id").GetGuid()}/finalize", null, token))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        async Task<decimal> DebtAsync() =>
            (await (await _client.GetAsync($"/api/v1/rooms/{roomId}", token)).ReadAsync<JsonElement>()).GetProperty("outstandingAmount").GetDecimal();
        var debt = await DebtAsync();
        var before = await DetailAsync(token, contractId);

        var url = $"/api/v1/contracts/{contractId}/representative";
        (await (await _client.PostJsonAsync(url, new { renterId = oldRepresentative, date = today }, token)).ReadProblemCodeAsync())
            .Should().Be("REPRESENTATIVE_UNCHANGED");
        (await (await _client.PostJsonAsync(url, new { renterId = await RenterWithoutIdAsync(token), date = today }, token)).ReadProblemCodeAsync())
            .Should().Be("REPRESENTATIVE_ID_REQUIRED");
        var changed = await _client.PostJsonAsync(url, new { renterId = successor, date = today, oldRepresentativeMovesOut = true }, token);
        changed.StatusCode.Should().Be(HttpStatusCode.NoContent, await changed.Content.ReadAsStringAsync());

        var after = await DetailAsync(token, contractId);
        after.GetProperty("contractNo").GetString().Should().Be(before.GetProperty("contractNo").GetString(), "HĐ giữ nguyên");
        after.GetProperty("representativeRenterId").GetGuid().Should().Be(successor);
        after.GetProperty("representativeSince").GetString().Should().Be($"{today:yyyy-MM-dd}");
        var history = after.GetProperty("representativeHistory").EnumerateArray().Single();
        history.GetProperty("renterId").GetGuid().Should().Be(oldRepresentative);
        history.GetProperty("fromDate").GetString().Should().Be($"{month:yyyy-MM-dd}");
        after.GetProperty("hasSignedDocument").GetBoolean().Should().BeFalse("bản ký cũ không còn đúng người đứng tên");
        OccupantOf(after, oldRepresentative).GetProperty("moveOutDate").GetString().Should().Be($"{today:yyyy-MM-dd}");
        OccupantOf(after, successor).GetProperty("relationshipType").ValueKind.Should().Be(JsonValueKind.Null);
        after.GetProperty("flags").EnumerateArray().Select(f => f.GetString()).Should().NotContain("RepresentativeMovedOut");
        (await DebtAsync()).Should().Be(debt, "nợ ở lại trên HĐ — người đứng tên mới nhận");
    }

    [Fact]
    public async Task MoveOccupant_ToAnotherActiveContract_EndsOldStay_StartsNewOne_TakesVehicleAlong()
    {
        var owner = await _client.CreateActiveOwnerAsync();
        var token = owner.Tokens.AccessToken;
        var today = TestData.Today(factory);
        var start = today.AddDays(-10);
        var propertyId = await _client.CreatePropertyAsync(token);
        var mover = await _client.CreateRenterAsync(token);
        var sourceRepresentative = await _client.CreateRenterAsync(token);
        var source = await ActiveContractAsync(token, await _client.CreateRoomAsync(token, propertyId), sourceRepresentative, start,
            Occupant(sourceRepresentative), Occupant(mover, relationshipType: "CoTenant"));
        var targetRepresentative = await _client.CreateRenterAsync(token);
        var target = await ActiveContractAsync(token, await _client.CreateRoomAsync(token, propertyId), targetRepresentative, start, Occupant(targetRepresentative));
        var draft = await (await PostContractAsync(token, await _client.CreateRoomAsync(token, propertyId), await _client.CreateRenterAsync(token), today)).ReadIdAsync();
        var plate = $"29B1{Random.Shared.Next(10000, 99999)}";
        (await _client.PostJsonAsync($"/api/v1/contracts/{source}/vehicles", new { renterId = mover, vehicleType = "Motorbike", plateNumber = plate, registeredFrom = start }, token))
            .StatusCode.Should().Be(HttpStatusCode.Created);

        var occupancy = OccupantOf(await DetailAsync(token, source), mover).GetProperty("id").GetGuid();
        var url = $"/api/v1/contracts/{source}/occupants/{occupancy}/move";
        (await (await _client.PostJsonAsync(url, new { toContractId = draft, date = today }, token)).ReadProblemCodeAsync()).Should().Be("CONTRACT_NOT_ACTIVE");
        (await (await _client.PostJsonAsync(url, new { toContractId = source, date = today }, token)).ReadProblemCodeAsync()).Should().Be("MOVE_SAME_CONTRACT");
        var moved = await _client.PostJsonAsync(url, new { toContractId = target, date = today, role = "CoTenant", relationshipType = "CoTenant" }, token);
        moved.StatusCode.Should().Be(HttpStatusCode.NoContent, await moved.Content.ReadAsStringAsync());

        var sourceAfter = await DetailAsync(token, source);
        OccupantOf(sourceAfter, mover).GetProperty("moveOutDate").GetString().Should().Be($"{today:yyyy-MM-dd}");
        sourceAfter.GetProperty("vehicles")[0].GetProperty("registeredTo").GetString().Should().Be($"{today:yyyy-MM-dd}");
        var targetAfter = await DetailAsync(token, target);
        var arrived = OccupantOf(targetAfter, mover);
        arrived.GetProperty("moveInDate").GetString().Should().Be($"{today:yyyy-MM-dd}");
        arrived.GetProperty("role").GetString().Should().Be("CoTenant");
        targetAfter.GetProperty("vehicles").EnumerateArray().Should().ContainSingle(v => v.GetProperty("plateNumber").GetString() == plate
            && v.GetProperty("registeredTo").ValueKind == JsonValueKind.Null);
    }
}
