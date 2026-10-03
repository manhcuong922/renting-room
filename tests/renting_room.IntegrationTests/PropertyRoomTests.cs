using System.Net;
using renting_room.IntegrationTests.Infrastructure;

namespace renting_room.IntegrationTests;

public sealed record PropertyDetail(Guid Id, string Code, LessorDetail? Lessor, string Version);

public sealed record LessorDetail(string Name, string? IdNumberMasked, bool IsComplete);

public sealed record RoomResponse(Guid Id, string Code, int MaxOccupants, string Status, CurrentContract? CurrentContract, string Version);

public sealed record CurrentContract(Guid Id, string ContractNo, int OccupantCount);

public sealed record RoomGroupResponse(Guid Id, string Name, List<Guid> RoomIds);

[Collection(ApiCollection.Name)]
public sealed class PropertyRoomTests(ApiFactory factory)
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task Property_LessorIdNumber_IsMaskedAndEncrypted_RevealIsAvailable()
    {
        var owner = await _client.CreateActiveOwnerAsync();
        var token = owner.Tokens.AccessToken;
        var propertyId = await _client.CreatePropertyAsync(token, withLessor: false);
        var idNumber = TestData.NewCitizenId();

        var put = await _client.PutJsonAsync($"/api/v1/properties/{propertyId}/lessor", new
        {
            type = "Individual", name = "Chủ Nhà", address = "Hà Nội", phone = "0911222333",
            idType = "CitizenId", idNumber, dateOfBirth = "1975-01-01"
        }, token);
        var detail = await put.ReadAsync<PropertyDetail>();

        detail.Lessor!.IdNumberMasked.Should().Be($"********{idNumber[^4..]}");
        detail.Lessor.IsComplete.Should().BeTrue();

        var reveal = await _client.PostJsonAsync($"/api/v1/properties/{propertyId}/lessor/reveal-id-number", null, token);
        (await reveal.Content.ReadAsStringAsync()).Should().Contain(idNumber);
    }

    [Fact]
    public async Task Lessor_Underage_IsRejected()
    {
        var owner = await _client.CreateActiveOwnerAsync();
        var propertyId = await _client.CreatePropertyAsync(owner.Tokens.AccessToken, withLessor: false);

        var put = await _client.PutJsonAsync($"/api/v1/properties/{propertyId}/lessor", new
        {
            type = "Individual", name = "Trẻ", address = "Hà Nội", phone = "0911222333",
            idType = "CitizenId", idNumber = TestData.NewCitizenId(), dateOfBirth = TestData.Today(factory).AddYears(-17).ToString("yyyy-MM-dd")
        }, owner.Tokens.AccessToken);

        put.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await put.Content.ReadAsStringAsync()).Should().Contain("dateOfBirth");
    }

    [Fact]
    public async Task RoomCode_IsUniquePerProperty_CaseInsensitive()
    {
        var owner = await _client.CreateActiveOwnerAsync();
        var token = owner.Tokens.AccessToken;
        var propertyId = await _client.CreatePropertyAsync(token);
        await _client.CreateRoomAsync(token, propertyId, "a101");

        var duplicate = await _client.PostJsonAsync($"/api/v1/properties/{propertyId}/rooms", new { code = "A101", spec = new { maxOccupants = 2 } }, token);
        (await duplicate.ReadProblemCodeAsync()).Should().Be("ROOM_CODE_TAKEN");

        // Khu khác được dùng cùng mã.
        var otherProperty = await _client.CreatePropertyAsync(token);
        await _client.CreateRoomAsync(token, otherProperty, "A101");
    }

    [Fact]
    public async Task BulkCreate_IsAllOrNothing()
    {
        var owner = await _client.CreateActiveOwnerAsync();
        var token = owner.Tokens.AccessToken;
        var propertyId = await _client.CreatePropertyAsync(token);
        await _client.CreateRoomAsync(token, propertyId, "201");

        var conflict = await _client.PostJsonAsync($"/api/v1/properties/{propertyId}/rooms/bulk", new
        {
            floors = new[] { new { floor = "1", codes = new[] { "101", "102" } }, new { floor = "2", codes = new[] { "201", "202" } } },
            maxOccupants = 3
        }, token);
        (await conflict.ReadProblemCodeAsync()).Should().Be("ROOM_CODE_TAKEN");

        var ok = await _client.PostJsonAsync($"/api/v1/properties/{propertyId}/rooms/bulk", new
        {
            floors = new[] { new { floor = "1", codes = new[] { "101", "102", "103" } } },
            maxOccupants = 3
        }, token);
        ok.StatusCode.Should().Be(HttpStatusCode.Created);

        var page = await (await _client.GetAsync($"/api/v1/rooms?propertyId={propertyId}", token)).ReadAsync<Page<RoomResponse>>();
        page.TotalCount.Should().Be(4);
    }

    [Fact]
    public async Task RoomStatus_IsDerivedFromContracts()
    {
        var owner = await _client.CreateActiveOwnerAsync();
        var token = owner.Tokens.AccessToken;
        var propertyId = await _client.CreatePropertyAsync(token);
        var roomId = await _client.CreateRoomAsync(token, propertyId);
        var renterId = await _client.CreateRenterAsync(token);

        async Task<RoomResponse> Room() => await (await _client.GetAsync($"/api/v1/rooms/{roomId}", token)).ReadAsync<RoomResponse>();

        (await Room()).Status.Should().Be("Vacant");

        var contractId = await _client.CreateContractAsync(token, roomId, renterId, TestData.Today(factory));
        (await Room()).Status.Should().Be("Reserved");

        await _client.PostJsonAsync($"/api/v1/contracts/{contractId}/activate", null, token);
        var occupied = await Room();
        occupied.Status.Should().Be("Occupied");
        occupied.CurrentContract!.OccupantCount.Should().Be(1);

        var filtered = await (await _client.GetAsync($"/api/v1/rooms?propertyId={propertyId}&status=Occupied", token)).ReadAsync<Page<RoomResponse>>();
        filtered.Items.Should().ContainSingle(r => r.Id == roomId);
    }

    [Fact]
    public async Task OccupiedRoom_CannotGoToMaintenanceOrArchive()
    {
        var owner = await _client.CreateActiveOwnerAsync();
        var token = owner.Tokens.AccessToken;
        var (propertyId, roomId, _, _) = await _client.CreateActiveContractAsync(token, TestData.Today(factory));

        (await (await _client.PostJsonAsync($"/api/v1/rooms/{roomId}/maintenance/start", new { note = "Sơn lại" }, token))
            .ReadProblemCodeAsync()).Should().Be("ROOM_OCCUPIED");
        (await (await _client.PostJsonAsync($"/api/v1/rooms/{roomId}/archive", null, token))
            .ReadProblemCodeAsync()).Should().Be("ROOM_HAS_CONTRACTS");
        (await (await _client.PostJsonAsync($"/api/v1/properties/{propertyId}/archive", null, token))
            .ReadProblemCodeAsync()).Should().Be("PROPERTY_HAS_ACTIVE_CONTRACTS");
    }

    [Fact]
    public async Task UpdateRoom_WithStaleVersion_Returns409()
    {
        var owner = await _client.CreateActiveOwnerAsync();
        var token = owner.Tokens.AccessToken;
        var roomId = await _client.CreateRoomAsync(token, await _client.CreatePropertyAsync(token));
        var room = await (await _client.GetAsync($"/api/v1/rooms/{roomId}", token)).ReadAsync<RoomResponse>();

        var first = await _client.PutJsonAsync($"/api/v1/rooms/{roomId}", new { code = room.Code, spec = new { maxOccupants = 3 }, version = room.Version }, token);
        first.StatusCode.Should().Be(HttpStatusCode.OK);

        var stale = await _client.PutJsonAsync($"/api/v1/rooms/{roomId}", new { code = room.Code, spec = new { maxOccupants = 4 }, version = room.Version }, token);
        (await stale.ReadProblemCodeAsync()).Should().Be("CONCURRENCY_CONFLICT");
    }

    [Fact]
    public async Task RoomGroup_OnlyAcceptsRoomsOfSameProperty()
    {
        var owner = await _client.CreateActiveOwnerAsync();
        var token = owner.Tokens.AccessToken;
        var propertyA = await _client.CreatePropertyAsync(token);
        var propertyB = await _client.CreatePropertyAsync(token);
        var roomA = await _client.CreateRoomAsync(token, propertyA);
        var roomB = await _client.CreateRoomAsync(token, propertyB);

        var created = await _client.PostJsonAsync($"/api/v1/properties/{propertyA}/room-groups", new { name = "Tầng 3" }, token);
        var group = await created.ReadAsync<RoomGroupResponse>();

        var wrong = await _client.PutJsonAsync($"/api/v1/room-groups/{group.Id}/members", new { roomIds = new[] { roomA, roomB } }, token);
        (await wrong.ReadProblemCodeAsync()).Should().Be("ROOM_NOT_IN_PROPERTY");

        var ok = await _client.PutJsonAsync($"/api/v1/room-groups/{group.Id}/members", new { roomIds = new[] { roomA } }, token);
        (await ok.ReadAsync<RoomGroupResponse>()).RoomIds.Should().Equal(roomA);

        var byGroup = await (await _client.GetAsync($"/api/v1/rooms?groupId={group.Id}", token)).ReadAsync<Page<RoomResponse>>();
        byGroup.Items.Should().ContainSingle(r => r.Id == roomA);
    }

    [Fact]
    public async Task Renter_DuplicateIdNumber_IsRejected_AndSearchWorksWithoutDiacritics()
    {
        var owner = await _client.CreateActiveOwnerAsync();
        var token = owner.Tokens.AccessToken;
        var idNumber = TestData.NewCitizenId();
        await _client.CreateRenterAsync(token, idNumber);

        var duplicate = await _client.PostJsonAsync("/api/v1/renters", new
        {
            fullName = "Người khác", dateOfBirth = "1999-01-01", gender = "Male", idType = "CitizenId",
            idNumber = $"{idNumber[..3]} {idNumber[3..6]} {idNumber[6..]}" // cùng số, khác định dạng
        }, token);
        (await duplicate.ReadProblemCodeAsync()).Should().Be("RENTER_ID_NUMBER_EXISTS");

        var search = await (await _client.GetAsync("/api/v1/renters?q=tran%20thi%20lan", token)).ReadAsync<Page<object>>();
        search.TotalCount.Should().Be(1);
    }

    [Fact]
    public async Task Renter_ChangingIdTypeWithoutNumber_IsRejected()
    {
        var owner = await _client.CreateActiveOwnerAsync();
        var token = owner.Tokens.AccessToken;
        var renterId = await _client.CreateRenterAsync(token);
        var renter = await (await _client.GetAsync($"/api/v1/renters/{renterId}", token)).ReadAsync<RenterVersion>();

        var response = await _client.PutJsonAsync($"/api/v1/renters/{renterId}", new
        {
            renter = new { fullName = "Trần Thị Lan", dateOfBirth = "2000-04-15", gender = "Female", idType = "Passport" },
            version = renter.Version
        }, token);

        (await response.ReadProblemCodeAsync()).Should().Be("ID_NUMBER_REQUIRED");
    }

    [Fact]
    public async Task Renter_SearchByPassport_WithoutSpecifyingType()
    {
        var owner = await _client.CreateActiveOwnerAsync();
        var token = owner.Tokens.AccessToken;
        var passport = $"M{Random.Shared.Next(1_000_000, 9_999_999)}";
        await (await _client.PostJsonAsync("/api/v1/renters", new
        {
            fullName = "Kim Min Ji", dateOfBirth = "1998-03-03", gender = "Female", nationality = "KR", idType = "Passport", idNumber = passport
        }, token)).ReadIdAsync();

        var found = await (await _client.GetAsync($"/api/v1/renters?idNumber={passport}", token)).ReadAsync<Page<object>>();

        found.TotalCount.Should().Be(1);
    }
}
