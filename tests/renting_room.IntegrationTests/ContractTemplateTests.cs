using System.Net;
using System.Text.Json;
using renting_room.IntegrationTests.Infrastructure;

namespace renting_room.IntegrationTests;

public sealed record TemplateField(string Key, string Label, string Type, bool Required, List<string>? Options);

public sealed record TemplateClause(string Heading, string Body);

public sealed record TemplateResponse(
    Guid Id, string Name, string ContractType, string Title, List<TemplateClause> Clauses, List<TemplateField> Fields,
    bool IsArchived, string Version);

public sealed record DocumentResponse(
    Guid? TemplateId, string ContractType, string Title, List<TemplateClause> Clauses,
    List<TemplateField> CustomFieldDefinitions, Dictionary<string, JsonElement> CustomFields);

public sealed record ContractWithDocument(Guid Id, string Status, DocumentResponse Document, string Version);

[Collection(ApiCollection.Name)]
public sealed class ContractTemplateTests(ApiFactory factory)
{
    private const string Templates = "/api/v1/contract-templates";
    private readonly HttpClient _client = factory.CreateClient();

    private async Task<Guid> CreateRoomRentalTemplateAsync(string token, string name)
    {
        var presets = await (await _client.GetAsync($"{Templates}/presets", token)).ReadAsync<List<JsonElement>>();
        var roomPreset = presets.Single(p => p.GetProperty("contractType").GetString() == "RoomRental" && !p.GetProperty("noDeposit").GetBoolean());
        var body = JsonSerializer.Deserialize<Dictionary<string, object>>(roomPreset.GetRawText())!;
        body["name"] = name;
        return await (await _client.PostJsonAsync(Templates, body, token)).ReadIdAsync();
    }

    private Task<HttpResponseMessage> PostContractWithTemplateAsync(
        string token, Guid roomId, Guid renterId, DateOnly start, Guid templateId, object customFields) =>
        _client.PostJsonAsync("/api/v1/contracts", new
        {
            roomId,
            contract = new
            {
                representativeRenterId = renterId,
                startDate = start,
                monthlyRent = 1_000_000,
                templateId,
                customFields,
                occupants = new[] { new { renterId } }
            }
        }, token);

    private static object ValidUtilityFields => new
    {
        electricity_pricing = "Theo giá nhà nước (bậc thang EVN)",
        electricity_payment_time = "Cuối tháng",
        water_pricing = "Theo đầu người",
        water_unit_price = 20_000,
        water_payment_time = "Đầu tháng",
        wifi_included = true
    };

    [Fact]
    public async Task Presets_CoverRoomAndWholeHouseRental()
    {
        var owner = await _client.CreateActiveOwnerAsync();

        var presets = await (await _client.GetAsync($"{Templates}/presets", owner.Tokens.AccessToken)).ReadAsync<List<TemplateResponse>>();

        presets.Select(p => p.ContractType).Should().BeEquivalentTo("RoomRental", "RoomRental", "WholeHouseRental");
        presets.Should().OnlyContain(p => p.Clauses.Any(c => c.Heading == "Giải quyết tranh chấp"));
    }

    [Fact]
    public async Task ContractFromTemplate_CopiesDocument_AndStoresTypedCustomFields()
    {
        var owner = await _client.CreateActiveOwnerAsync();
        var token = owner.Tokens.AccessToken;
        var today = TestData.Today(factory);
        var templateId = await CreateRoomRentalTemplateAsync(token, "Mẫu thuê trọ");
        var roomId = await _client.CreateRoomAsync(token, await _client.CreatePropertyAsync(token));
        var renterId = await _client.CreateRenterAsync(token);

        var contractId = await (await PostContractWithTemplateAsync(token, roomId, renterId, today, templateId, ValidUtilityFields)).ReadIdAsync();
        await _client.PostJsonAsync($"/api/v1/contracts/{contractId}/activate", null, token);

        var contract = await (await _client.GetAsync($"/api/v1/contracts/{contractId}", token)).ReadAsync<ContractWithDocument>();
        contract.Status.Should().Be("Active");
        contract.Document.TemplateId.Should().Be(templateId);
        contract.Document.ContractType.Should().Be("RoomRental");
        contract.Document.Title.Should().Be("HỢP ĐỒNG THUÊ PHÒNG TRỌ");
        contract.Document.Clauses.Should().HaveCount(4);
        contract.Document.CustomFieldDefinitions.Should().Contain(f => f.Key == "water_unit_price" && f.Type == "Money");
        contract.Document.CustomFields["water_unit_price"].GetDecimal().Should().Be(20_000);
        contract.Document.CustomFields["wifi_included"].GetBoolean().Should().BeTrue();

        // Sửa mẫu sau khi ký không đổi hợp đồng đã kích hoạt.
        var template = await (await _client.GetAsync($"{Templates}/{templateId}", token)).ReadAsync<TemplateResponse>();
        (await _client.PutJsonAsync($"{Templates}/{templateId}", new
        {
            name = template.Name, contractType = "RoomRental", title = "HỢP ĐỒNG MỚI", clauses = Array.Empty<object>(),
            fields = Array.Empty<object>(), version = template.Version
        }, token)).StatusCode.Should().Be(HttpStatusCode.OK);
        var after = await (await _client.GetAsync($"/api/v1/contracts/{contractId}", token)).ReadAsync<ContractWithDocument>();
        after.Document.Title.Should().Be("HỢP ĐỒNG THUÊ PHÒNG TRỌ");
        after.Document.CustomFieldDefinitions.Should().NotBeEmpty();
    }

    [Fact]
    public async Task InvalidCustomFields_Return400_KeyedByBodyPath()
    {
        var owner = await _client.CreateActiveOwnerAsync();
        var token = owner.Tokens.AccessToken;
        var templateId = await CreateRoomRentalTemplateAsync(token, "Mẫu kiểm tra");
        var roomId = await _client.CreateRoomAsync(token, await _client.CreatePropertyAsync(token));
        var renterId = await _client.CreateRenterAsync(token);

        var response = await PostContractWithTemplateAsync(token, roomId, renterId, TestData.Today(factory), templateId,
            new { electricity_pricing = "Miễn phí", water_pricing = "Theo đầu người", not_in_template = 1 });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        problem.RootElement.GetProperty("errors").EnumerateObject().Select(p => p.Name).Should().BeEquivalentTo(
            "contract.customFields.electricity_pricing",
            "contract.customFields.water_unit_price",
            "contract.customFields.not_in_template");
    }

    [Fact]
    public async Task ArchivedTemplate_CannotStartNewContract_ButExistingDraftKeepsIt()
    {
        var owner = await _client.CreateActiveOwnerAsync();
        var token = owner.Tokens.AccessToken;
        var today = TestData.Today(factory);
        var templateId = await CreateRoomRentalTemplateAsync(token, "Mẫu cũ");
        var propertyId = await _client.CreatePropertyAsync(token);
        var renterId = await _client.CreateRenterAsync(token);
        var draftId = await (await PostContractWithTemplateAsync(
            token, await _client.CreateRoomAsync(token, propertyId), renterId, today, templateId, ValidUtilityFields)).ReadIdAsync();

        (await _client.PostJsonAsync($"{Templates}/{templateId}/archive", null, token)).StatusCode.Should().Be(HttpStatusCode.NoContent);

        (await (await PostContractWithTemplateAsync(token, await _client.CreateRoomAsync(token, propertyId), renterId, today, templateId,
            ValidUtilityFields)).ReadProblemCodeAsync()).Should().Be("CONTRACT_TEMPLATE_ARCHIVED");

        var draft = await (await _client.GetAsync($"/api/v1/contracts/{draftId}", token)).ReadAsync<ContractWithDocument>();
        (await _client.PutJsonAsync($"/api/v1/contracts/{draftId}", new
        {
            contract = new
            {
                representativeRenterId = renterId, startDate = today, monthlyRent = 1_100_000, templateId,
                customFields = ValidUtilityFields, occupants = new[] { new { renterId } }
            },
            version = draft.Version
        }, token)).StatusCode.Should().Be(HttpStatusCode.NoContent);

        var list = await (await _client.GetAsync($"{Templates}", token)).ReadAsync<List<TemplateResponse>>();
        list.Should().NotContain(t => t.Id == templateId, "mẫu ngừng dùng bị ẩn mặc định");
    }

    [Fact]
    public async Task ContractWithoutTemplate_GetsDefaultTitle_AndRejectsCustomFields()
    {
        var owner = await _client.CreateActiveOwnerAsync();
        var token = owner.Tokens.AccessToken;
        var roomId = await _client.CreateRoomAsync(token, await _client.CreatePropertyAsync(token));
        var renterId = await _client.CreateRenterAsync(token);

        var contractId = await _client.CreateContractAsync(token, roomId, renterId, TestData.Today(factory));
        var contract = await (await _client.GetAsync($"/api/v1/contracts/{contractId}", token)).ReadAsync<ContractWithDocument>();
        contract.Document.Should().BeEquivalentTo(new
        {
            TemplateId = (Guid?)null, ContractType = "RoomRental", Title = "HỢP ĐỒNG THUÊ PHÒNG TRỌ"
        }, o => o.ExcludingMissingMembers());
        contract.Document.CustomFields.Should().BeEmpty();

        var withFields = await _client.PostJsonAsync("/api/v1/contracts", new
        {
            roomId,
            contract = new
            {
                representativeRenterId = renterId, startDate = TestData.Today(factory), contractType = "WholeHouseRental",
                customFields = new { floor_count = 2 }
            }
        }, token);
        (await withFields.ReadProblemCodeAsync()).Should().Be("VALIDATION_FAILED");
    }

    [Fact]
    public async Task NoDepositTemplate_ForcesZeroDeposit_AndContractsAreFilterableByDeposit()
    {
        var owner = await _client.CreateActiveOwnerAsync();
        var token = owner.Tokens.AccessToken;
        var today = TestData.Today(factory);
        var propertyId = await _client.CreatePropertyAsync(token);
        var renterId = await _client.CreateRenterAsync(token);
        var templateId = await (await _client.PostJsonAsync(Templates, new
        {
            name = "Không cọc", contractType = "RoomRental", title = "HỢP ĐỒNG THUÊ PHÒNG TRỌ", noDeposit = true
        }, token)).ReadIdAsync();

        // Phòng có cọc mặc định nhưng mẫu không cọc ⇒ cọc = 0.
        var roomWithDefaultDeposit = await (await _client.PostJsonAsync($"/api/v1/properties/{propertyId}/rooms", new
        {
            code = "K01", spec = new { maxOccupants = 2, listedRent = 1_000_000, defaultDeposit = 1_000_000 }
        }, token)).ReadIdAsync();
        var contract = new { representativeRenterId = renterId, startDate = today, templateId, occupants = new[] { new { renterId } } };
        var noDepositId = await (await _client.PostJsonAsync("/api/v1/contracts",
            new { roomId = roomWithDefaultDeposit, contract }, token)).ReadIdAsync();

        var withDeposit = await _client.PostJsonAsync("/api/v1/contracts", new
        {
            roomId = await _client.CreateRoomAsync(token, propertyId),
            contract = new { representativeRenterId = renterId, startDate = today, templateId, depositAmount = 500_000 }
        }, token);
        (await withDeposit.ReadProblemCodeAsync()).Should().Be("DEPOSIT_NOT_ALLOWED");

        var depositedId = await _client.CreateContractAsync(token, await _client.CreateRoomAsync(token, propertyId), renterId, today);

        var noDepositGroup = await (await _client.GetAsync("/api/v1/contracts?hasDeposit=false", token)).ReadAsync<Page<JsonElement>>();
        noDepositGroup.Items.Select(i => i.GetProperty("id").GetGuid()).Should().Equal(noDepositId);
        noDepositGroup.Items.Single().GetProperty("depositAmount").GetDecimal().Should().Be(0);
        var depositGroup = await (await _client.GetAsync("/api/v1/contracts?hasDeposit=true", token)).ReadAsync<Page<JsonElement>>();
        depositGroup.Items.Select(i => i.GetProperty("id").GetGuid()).Should().Equal(depositedId);
    }

    [Fact]
    public async Task TemplateNames_AreUniquePerOrganization_AndIsolatedBetweenOrganizations()
    {
        var owner = await _client.CreateActiveOwnerAsync();
        var other = await _client.CreateActiveOwnerAsync();
        var templateId = await CreateRoomRentalTemplateAsync(owner.Tokens.AccessToken, "Trùng tên");

        var duplicate = await _client.PostJsonAsync(Templates,
            new { name = "Trùng tên", contractType = "RoomRental", title = "HĐ" }, owner.Tokens.AccessToken);
        (await duplicate.ReadProblemCodeAsync()).Should().Be("CONTRACT_TEMPLATE_NAME_TAKEN");

        (await _client.GetAsync($"{Templates}/{templateId}", other.Tokens.AccessToken)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        await CreateRoomRentalTemplateAsync(other.Tokens.AccessToken, "Trùng tên");
    }
}
