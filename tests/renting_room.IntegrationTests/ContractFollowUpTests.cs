using System.Net;
using System.Text.Json;
using ClosedXML.Excel;
using renting_room.IntegrationTests.Infrastructure;

namespace renting_room.IntegrationTests;

/// <summary>Các mục plan M05 trước đây ghi "chưa có / P2 / câu hỏi mở" (nhóm A–F, 03/10/2026).</summary>
[Collection(ApiCollection.Name)]
public sealed class ContractFollowUpTests(ApiFactory factory)
{
    private readonly HttpClient _client = factory.CreateClient();

    private async Task<Guid> RenterAsync(string token, string fullName, string dateOfBirth, string gender = "Male") =>
        await (await _client.PostJsonAsync("/api/v1/renters", new
        {
            fullName, dateOfBirth, gender, phone = "0912345678", idType = "CitizenId", idNumber = TestData.NewCitizenId()
        }, token)).ReadIdAsync();

    private async Task<JsonElement> DetailAsync(string token, Guid contractId) =>
        await (await _client.GetAsync($"/api/v1/contracts/{contractId}", token)).ReadAsync<JsonElement>();

    private static IEnumerable<string?> WarningCodes(JsonElement body) =>
        body.GetProperty("warnings").EnumerateArray().Select(w => w.GetProperty("code").GetString());

    // ---------------------------------------------------------------- A
    [Fact]
    public async Task A_NoteCanBeEditedOnActiveContract_ButNotOnCancelled()
    {
        var owner = await _client.CreateActiveOwnerAsync();
        var token = owner.Tokens.AccessToken;
        var (_, roomId, renterId, contractId) = await _client.CreateActiveContractAsync(token, TestData.Today(factory));

        (await _client.PutJsonAsync($"/api/v1/contracts/{contractId}/note", new { note = "Khách hẹn trả tiền ngày 10" }, token))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await DetailAsync(token, contractId)).GetProperty("note").GetString().Should().Be("Khách hẹn trả tiền ngày 10");

        var draftId = await _client.CreateContractAsync(token, await _client.CreateRoomAsync(token, (await DetailAsync(token, contractId)).GetProperty("propertyId").GetGuid()), renterId, TestData.Today(factory));
        await _client.PostJsonAsync($"/api/v1/contracts/{draftId}/cancel", new { reason = "Khách đổi ý" }, token);
        (await (await _client.PutJsonAsync($"/api/v1/contracts/{draftId}/note", new { note = "x" }, token)).ReadProblemCodeAsync())
            .Should().Be("CONTRACT_NOT_EDITABLE");
    }

    // ---------------------------------------------------------------- B
    [Fact]
    public async Task B_HighDepositAndUnusualPlate_ReturnWarnings_WithoutBlocking()
    {
        var owner = await _client.CreateActiveOwnerAsync();
        var token = owner.Tokens.AccessToken;
        var roomId = await _client.CreateRoomAsync(token, await _client.CreatePropertyAsync(token));
        var renterId = await _client.CreateRenterAsync(token);

        var created = await _client.PostJsonAsync("/api/v1/contracts", new
        {
            roomId,
            contract = new { representativeRenterId = renterId, startDate = TestData.Today(factory), monthlyRent = 1_000_000, depositAmount = 4_000_000 }
        }, token);
        created.StatusCode.Should().Be(HttpStatusCode.Created);
        var body = await created.ReadAsync<JsonElement>();
        WarningCodes(body).Should().Equal("DEPOSIT_ABOVE_THREE_MONTHS");
        var contractId = body.GetProperty("id").GetGuid();

        var vehicle = await _client.PostJsonAsync($"/api/v1/contracts/{contractId}/vehicles",
            new { vehicleType = "Motorbike", plateNumber = "ABC-XYZ" }, token);
        vehicle.StatusCode.Should().Be(HttpStatusCode.Created);
        WarningCodes(await vehicle.ReadAsync<JsonElement>()).Should().Equal("PLATE_FORMAT_UNUSUAL");

        var ok = await _client.PostJsonAsync($"/api/v1/contracts/{contractId}/vehicles",
            new { vehicleType = "Motorbike", plateNumber = "29-B1 123.45" }, token);
        WarningCodes(await ok.ReadAsync<JsonElement>()).Should().BeEmpty();

        WarningCodes(await DetailAsync(token, contractId)).Should().BeEquivalentTo("DEPOSIT_ABOVE_THREE_MONTHS", "PLATE_FORMAT_UNUSUAL");
    }

    // ---------------------------------------------------------------- C
    [Fact]
    public async Task C_AddingOccupantAfterEndDate_AsksToExtendFirst()
    {
        var owner = await _client.CreateActiveOwnerAsync();
        var token = owner.Tokens.AccessToken;
        var today = TestData.Today(factory);
        var roomId = await _client.CreateRoomAsync(token, await _client.CreatePropertyAsync(token));
        var renterId = await _client.CreateRenterAsync(token);
        var contractId = await (await _client.PostJsonAsync("/api/v1/contracts", new
        {
            roomId,
            contract = new
            {
                representativeRenterId = renterId, startDate = today.AddDays(-60), endDate = today.AddDays(-1), monthlyRent = 3_000_000,
                occupants = new[] { new { renterId } }
            }
        }, token)).ReadIdAsync();
        await _client.PostJsonAsync($"/api/v1/contracts/{contractId}/activate", null, token);

        var add = await _client.PostJsonAsync($"/api/v1/contracts/{contractId}/occupants",
            new { renterId = await _client.CreateRenterAsync(token), moveInDate = today, relationshipType = "CoTenant" }, token);
        (await add.ReadProblemCodeAsync()).Should().Be("CONTRACT_EXPIRED_EXTEND_FIRST");
    }

    // ---------------------------------------------------------------- D
    [Fact]
    public async Task D_ChangingRepresentative_WhenVehicleBelongsToOldRepresentative_IsRejected()
    {
        var owner = await _client.CreateActiveOwnerAsync();
        var token = owner.Tokens.AccessToken;
        var today = TestData.Today(factory);
        var roomId = await _client.CreateRoomAsync(token, await _client.CreatePropertyAsync(token));
        var parent = await RenterAsync(token, "Phụ huynh", "1975-01-01");
        var student = await RenterAsync(token, "Sinh viên", "2005-01-01");
        var contractId = await _client.CreateContractAsync(token, roomId, parent, today, student);
        await _client.PostJsonAsync($"/api/v1/contracts/{contractId}/vehicles",
            new { renterId = parent, vehicleType = "Motorbike", plateNumber = "29B112345" }, token);

        var update = await _client.PutJsonAsync($"/api/v1/contracts/{contractId}", new
        {
            contract = new { representativeRenterId = student, startDate = today, monthlyRent = 3_500_000, occupants = new[] { new { renterId = student } } },
            version = (await DetailAsync(token, contractId)).GetProperty("version").GetString()
        }, token);
        (await update.ReadProblemCodeAsync()).Should().Be("VEHICLE_OWNER_NOT_IN_CONTRACT");
    }

    // ---------------------------------------------------------------- E
    [Fact]
    public async Task E_HouseholdHead_IsTheReferenceForRelationships_WhenRepresentativeDoesNotLiveThere()
    {
        var owner = await _client.CreateActiveOwnerAsync();
        var token = owner.Tokens.AccessToken;
        var today = TestData.Today(factory);
        var propertyId = await _client.CreatePropertyAsync(token);
        var roomId = await _client.CreateRoomAsync(token, propertyId);
        var parent = await RenterAsync(token, "Phụ huynh", "1975-01-01");
        var elder = await RenterAsync(token, "Anh cả", "2004-01-01");
        var younger = await RenterAsync(token, "Em út", today.AddYears(-17).ToString("yyyy-MM-dd"));
        object Occupants() => new object[]
        {
            new { renterId = elder },
            new { renterId = younger, relationshipType = "Sibling", guardianConsent = true }
        };

        // Không chọn chủ hộ ⇒ quan hệ so với người đứng tên (không ở cùng) ⇒ anh cả thiếu quan hệ.
        var withoutHead = await _client.PostJsonAsync("/api/v1/contracts", new
        {
            roomId, contract = new { representativeRenterId = parent, startDate = today, monthlyRent = 3_000_000, occupants = Occupants() }
        }, token);
        withoutHead.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var notOccupant = await _client.PostJsonAsync("/api/v1/contracts", new
        {
            roomId, contract = new { representativeRenterId = parent, startDate = today, monthlyRent = 3_000_000, occupants = Occupants(), householdHeadRenterId = parent }
        }, token);
        using (var problem = JsonDocument.Parse(await notOccupant.Content.ReadAsStringAsync()))
            problem.RootElement.GetProperty("errors").EnumerateObject().Select(p => p.Name).Should().Equal("contract.householdHeadRenterId");

        var contractId = await (await _client.PostJsonAsync("/api/v1/contracts", new
        {
            roomId, contract = new { representativeRenterId = parent, startDate = today, monthlyRent = 3_000_000, occupants = Occupants(), householdHeadRenterId = elder }
        }, token)).ReadIdAsync();
        (await _client.PostJsonAsync($"/api/v1/contracts/{contractId}/activate", null, token)).StatusCode.Should().Be(HttpStatusCode.NoContent);

        var detail = await DetailAsync(token, contractId);
        detail.GetProperty("householdHeadRenterId").GetGuid().Should().Be(elder);
        detail.GetProperty("occupants").EnumerateArray().Single(o => o.GetProperty("isHouseholdHead").GetBoolean())
            .GetProperty("renterId").GetGuid().Should().Be(elder);

        var export = await _client.PostJsonAsync("/api/v1/exports/renters", new { propertyIds = new[] { propertyId } }, token, idempotencyKey: null);
        using var book = new XLWorkbook(await export.Content.ReadAsStreamAsync());
        var sheet = book.Worksheets.Single();
        var column = sheet.Row(4).CellsUsed().Single(c => c.GetString().StartsWith("Quan hệ")).Address.ColumnNumber;
        new[] { sheet.Cell(5, column).GetString(), sheet.Cell(6, column).GetString() }.Should().Equal("Chủ hộ", "Anh / chị / em ruột");
    }

    // ---------------------------------------------------------------- F
    [Fact]
    public async Task F_ParallelRequests_CannotPutOnePersonInTwoRooms()
    {
        var owner = await _client.CreateActiveOwnerAsync();
        var token = owner.Tokens.AccessToken;
        var today = TestData.Today(factory);
        var (_, _, _, roomA) = await _client.CreateActiveContractAsync(token, today);
        var (_, _, _, roomB) = await _client.CreateActiveContractAsync(token, today);
        var mover = await RenterAsync(token, "Người chuyển", "1995-01-01");
        var body = new { renterId = mover, moveInDate = today, relationshipType = "CoTenant" };

        var responses = await Task.WhenAll(
            _client.PostJsonAsync($"/api/v1/contracts/{roomA}/occupants", body, token),
            _client.PostJsonAsync($"/api/v1/contracts/{roomB}/occupants", body, token));

        responses.Select(r => r.StatusCode).Should().BeEquivalentTo([HttpStatusCode.NoContent, HttpStatusCode.Conflict]);
    }
}
