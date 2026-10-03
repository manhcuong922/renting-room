using System.Net;
using System.Text.Json;
using ClosedXML.Excel;
using renting_room.IntegrationTests.Infrastructure;

namespace renting_room.IntegrationTests;

public sealed record OccupantView(Guid RenterId, string FullName, bool IsRepresentative, string? RelationshipType, bool GuardianConsent);

public sealed record ContractOccupants(Guid Id, string Status, List<OccupantView> Occupants);

[Collection(ApiCollection.Name)]
public sealed class OccupantRelationshipTests(ApiFactory factory)
{
    private readonly HttpClient _client = factory.CreateClient();

    private async Task<Guid> RenterAsync(string token, string fullName, string dateOfBirth, string gender) =>
        await (await _client.PostJsonAsync("/api/v1/renters", new
        {
            fullName, dateOfBirth, gender, phone = "0912345678", idType = "CitizenId", idNumber = TestData.NewCitizenId()
        }, token)).ReadIdAsync();

    private Task<HttpResponseMessage> PostDraftAsync(string token, Guid roomId, Guid representativeId, params object[] occupants) =>
        _client.PostJsonAsync("/api/v1/contracts", new
        {
            roomId,
            contract = new { representativeRenterId = representativeId, startDate = TestData.Today(factory), monthlyRent = 3_000_000, occupants }
        }, token);

    private static async Task<IReadOnlyList<string>> ErrorKeysAsync(HttpResponseMessage response)
    {
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest, await response.Content.ReadAsStringAsync());
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return doc.RootElement.GetProperty("errors").EnumerateObject().Select(p => p.Name).ToList();
    }

    [Fact]
    public async Task Family_FatherSignsForWifeAndChild_IsActivated_AndExportedWithRelationships()
    {
        var owner = await _client.CreateActiveOwnerAsync();
        var token = owner.Tokens.AccessToken;
        var propertyId = await _client.CreatePropertyAsync(token);
        var roomId = await _client.CreateRoomAsync(token, propertyId, maxOccupants: 4);
        var father = await RenterAsync(token, "Nguyễn Văn Bố", "1985-05-01", "Male");
        var mother = await RenterAsync(token, "Trần Thị Mẹ", "1988-03-02", "Female");
        var child = await RenterAsync(token, "Nguyễn Văn Con", "2016-09-09", "Male");

        var contractId = await (await PostDraftAsync(token, roomId, father,
            new { renterId = father },
            new { renterId = mother, relationshipType = "Wife" },
            new { renterId = child, relationshipType = "Child" })).ReadIdAsync();
        (await _client.PostJsonAsync($"/api/v1/contracts/{contractId}/activate", null, token)).StatusCode.Should().Be(HttpStatusCode.NoContent);

        var contract = await (await _client.GetAsync($"/api/v1/contracts/{contractId}", token)).ReadAsync<ContractOccupants>();
        contract.Occupants.Should().ContainSingle(o => o.IsRepresentative && o.RenterId == father && o.RelationshipType == null);
        contract.Occupants.Should().Contain(o => o.RenterId == mother && o.RelationshipType == "Wife");

        var export = await _client.PostJsonAsync("/api/v1/exports/renters", new { propertyIds = new[] { propertyId } }, token, idempotencyKey: null);
        using var book = new XLWorkbook(await export.Content.ReadAsStreamAsync());
        var sheet = book.Worksheets.Single();
        var relationshipColumn = sheet.Row(4).CellsUsed().Single(c => c.GetString().StartsWith("Quan hệ")).Address.ColumnNumber;
        Enumerable.Range(5, 3).Select(r => sheet.Cell(r, relationshipColumn).GetString())
            .Should().Equal("Chủ hộ (người đứng tên)", "Vợ", "Con đẻ");
        sheet.Cell(7, relationshipColumn + 1).GetString().Should().Be("Có — cha/mẹ/giám hộ đứng tên HĐ");
    }

    [Fact]
    public async Task ImplausibleRelationships_AreRejectedPerOccupant()
    {
        var owner = await _client.CreateActiveOwnerAsync();
        var token = owner.Tokens.AccessToken;
        var roomId = await _client.CreateRoomAsync(token, await _client.CreatePropertyAsync(token), maxOccupants: 4);
        var father = await RenterAsync(token, "Nguyễn Văn Bố", "1985-05-01", "Male");
        var olderMan = await RenterAsync(token, "Lê Văn Già", "1970-01-01", "Male");
        var friend = await RenterAsync(token, "Phạm Văn Bạn", "1990-01-01", "Male");

        var keys = await ErrorKeysAsync(await PostDraftAsync(token, roomId, father,
            new { renterId = father },
            new { renterId = olderMan, relationshipType = "Child" },   // con lớn tuổi hơn cha
            new { renterId = friend, relationshipType = "Wife" },      // "vợ" là nam
            new { renterId = await RenterAsync(token, "Ai Đó", "1992-01-01", "Female") }));  // thiếu quan hệ

        keys.Should().BeEquivalentTo(
            "contract.occupants[1].relationshipType",
            "contract.occupants[2].relationshipType",
            "contract.occupants[3].relationshipType");
    }

    [Fact]
    public async Task MinorCoTenant_NeedsGuardianConsent()
    {
        var owner = await _client.CreateActiveOwnerAsync();
        var token = owner.Tokens.AccessToken;
        var today = TestData.Today(factory);
        var (_, _, _, contractId) = await _client.CreateActiveContractAsync(token, today);
        var teen = await RenterAsync(token, "Học Sinh", today.AddYears(-16).ToString("yyyy-MM-dd"), "Female");

        var missing = await _client.PostJsonAsync($"/api/v1/contracts/{contractId}/occupants",
            new { renterId = teen, moveInDate = today, relationshipType = "NephewNiece" }, token);
        (await ErrorKeysAsync(missing)).Should().Equal("guardianConsent");

        (await _client.PostJsonAsync($"/api/v1/contracts/{contractId}/occupants",
                new { renterId = teen, moveInDate = today, relationshipType = "NephewNiece", guardianConsent = true }, token))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task PersonCannotLiveInTwoRoomsAtOnce_ButCanMoveSameDay()
    {
        var owner = await _client.CreateActiveOwnerAsync();
        var token = owner.Tokens.AccessToken;
        var today = TestData.Today(factory);
        var (_, _, _, roomA) = await _client.CreateActiveContractAsync(token, today);
        var (_, _, _, roomB) = await _client.CreateActiveContractAsync(token, today);
        var mover = await RenterAsync(token, "Người Chuyển", "1995-01-01", "Male");
        var add = new { renterId = mover, moveInDate = today, relationshipType = "CoTenant" };
        (await _client.PostJsonAsync($"/api/v1/contracts/{roomA}/occupants", add, token)).StatusCode.Should().Be(HttpStatusCode.NoContent);

        var conflict = await _client.PostJsonAsync($"/api/v1/contracts/{roomB}/occupants", add, token);
        conflict.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await conflict.ReadProblemCodeAsync()).Should().Be("OCCUPANT_LIVES_ELSEWHERE");

        var occupantInA = (await (await _client.GetAsync($"/api/v1/contracts/{roomA}", token)).ReadAsync<ContractOccupants>())
            .Occupants.Single(o => o.RenterId == mover);
        var detailA = await (await _client.GetAsync($"/api/v1/contracts/{roomA}", token)).ReadAsync<JsonElement>();
        var occupantId = detailA.GetProperty("occupants").EnumerateArray()
            .Single(o => o.GetProperty("renterId").GetGuid() == occupantInA.RenterId).GetProperty("id").GetGuid();
        (await _client.PostJsonAsync($"/api/v1/contracts/{roomA}/occupants/{occupantId}/end", new { moveOutDate = today }, token))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);

        (await _client.PostJsonAsync($"/api/v1/contracts/{roomB}/occupants", add, token)).StatusCode.Should().Be(HttpStatusCode.NoContent);
    }
}
