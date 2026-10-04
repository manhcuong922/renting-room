using System.Net;
using System.Text.Json;
using DocumentFormat.OpenXml.Packaging;
using renting_room.IntegrationTests.Infrastructure;

namespace renting_room.IntegrationTests;

[Collection(ApiCollection.Name)]
public sealed class ContractDocumentTests(ApiFactory factory)
{
    private readonly HttpClient _client = factory.CreateClient();

    private async Task<string> DocumentTextAsync(string token, Guid contractId)
    {
        var response = await _client.GetAsync($"/api/v1/contracts/{contractId}/document", token);
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/vnd.openxmlformats-officedocument.wordprocessingml.document");
        using var stream = new MemoryStream(await response.Content.ReadAsByteArrayAsync());
        using var doc = WordprocessingDocument.Open(stream, false);
        return string.Join('\n', doc.MainDocumentPart!.Document.Body!.Descendants<DocumentFormat.OpenXml.Wordprocessing.Paragraph>().Select(p => p.InnerText));
    }

    [Fact]
    public async Task ActiveContract_PrintsPartiesRentInWordsFeesOccupantsAndSignatures()
    {
        var owner = await _client.CreateActiveOwnerAsync();
        var token = owner.Tokens.AccessToken;
        var today = TestData.Today(factory);
        var propertyId = await _client.CreatePropertyAsync(token);
        var waterPerPerson = await (await _client.PostJsonAsync($"/api/v1/properties/{propertyId}/fee-types", new
        {
            name = "Nước theo người", group = "Service", chargeBasis = "PerOccupant", unit = "người", autoAttach = false,
            initialPrice = new { effectiveFrom = today, unitPrice = 20000 }
        }, token)).ReadIdAsync();
        var fees = await (await _client.GetAsync($"/api/v1/properties/{propertyId}/fee-types", token)).ReadAsync<List<JsonElement>>();
        var electricity = fees.Single(f => f.GetProperty("name").GetString() == "Điện").GetProperty("id").GetGuid();
        await _client.PostJsonAsync($"/api/v1/fee-types/{electricity}/prices", new { effectiveFrom = today, unitPrice = 3500 }, token);

        var husbandId = TestData.NewCitizenId();
        var husband = await (await _client.PostJsonAsync("/api/v1/renters", new
        {
            fullName = "Phạm Văn Chồng", dateOfBirth = "1985-05-01", gender = "Male", phone = "0367733904", idType = "CitizenId",
            idNumber = husbandId, permanentAddress = "Xóm Hồng Sơn, Nghĩa Phúc, Nghệ An"
        }, token)).ReadIdAsync();
        var wife = await (await _client.PostJsonAsync("/api/v1/renters", new
        {
            fullName = "Phạm Thị Huệ", dateOfBirth = "1988-02-17", gender = "Female", idType = "CitizenId", idNumber = TestData.NewCitizenId()
        }, token)).ReadIdAsync();

        var roomId = await _client.CreateRoomAsync(token, propertyId);
        var meter = await _client.InstallMeterAsync(token, roomId, electricity, today, 1250);
        var contractId = await (await _client.PostJsonAsync("/api/v1/contracts", new
        {
            roomId,
            contract = new
            {
                representativeRenterId = husband, startDate = today, monthlyRent = 3_500_000, depositAmount = 0,
                occupants = new object[] { new { renterId = husband }, new { renterId = wife, relationshipType = "Wife" } },
                fees = new object[] { new { feeTypeId = waterPerPerson } }
            }
        }, token)).ReadIdAsync();
        (await _client.PostJsonAsync($"/api/v1/contracts/{contractId}/activate",
            new { handoverReadings = new[] { new { meterId = meter, value = (decimal?)null } } }, token)).StatusCode.Should().Be(HttpStatusCode.NoContent);

        var text = await DocumentTextAsync(token, contractId);

        text.Should().NotContain("Nước: theo chỉ số công tơ", "phòng tính nước theo người, không có công tơ nước");
        text.Should().Contain("CỘNG HÒA XÃ HỘI CHỦ NGHĨA VIỆT NAM").And.Contain("HỢP ĐỒNG THUÊ PHÒNG TRỌ")
            .And.Contain("Bằng chữ: Ba triệu năm trăm nghìn đồng")
            .And.Contain(husbandId, "văn bản in có số giấy tờ đầy đủ")
            .And.Contain("Nguyễn Văn Chủ")
            .And.Contain("Điện: theo chỉ số công tơ của phòng (tính từ chỉ số ngày nhận phòng), 3.500 đ/kWh")
            .And.Contain("Nước theo người: 20.000 đ/người/tháng")
            .And.Contain("Hai bên thỏa thuận không đặt cọc")
            .And.Contain("Vợ")
            .And.Contain("ĐẠI DIỆN BÊN A").And.Contain("ĐẠI DIỆN BÊN B");
        text.Should().NotContain("BẢN NHÁP");
    }

    [Fact]
    public async Task Print_UsesTermsAgreedAtSigning_ShowsLaterChanges_AndOnlyCurrentOccupants()
    {
        var owner = await _client.CreateActiveOwnerAsync();
        var token = owner.Tokens.AccessToken;
        var today = TestData.Today(factory);
        var start = today.AddDays(-10);
        var (propertyId, roomId, renterId, contractId) = await _client.CreateActiveContractAsync(token, start);
        var fees = await (await _client.GetAsync($"/api/v1/properties/{propertyId}/fee-types", token)).ReadAsync<List<JsonElement>>();
        var water = fees.Single(f => f.GetProperty("name").GetString() == "Nước").GetProperty("id").GetGuid();
        await _client.InstallMeterAsync(token, roomId, water, start); // lắp sau khi ký
        await _client.PostJsonAsync($"/api/v1/fee-types/{water}/prices", new { effectiveFrom = start, unitPrice = 15500.5 }, token);

        // Lúc ký nước chưa có giá ⇒ văn bản in lấy giá của khu tại ngày bắt đầu (15.500,5đ).
        // Sau khi ký: chủ trọ tăng giá nước của cả khu + thêm phí rác cho phòng từ kỳ sau; một người ở cùng đã chuyển đi.
        await _client.PostJsonAsync($"/api/v1/fee-types/{water}/prices", new { effectiveFrom = today, unitPrice = 30000 }, token);
        var garbage = await (await _client.PostJsonAsync($"/api/v1/properties/{propertyId}/fee-types", new
        {
            name = "Rác", group = "Service", chargeBasis = "PerRoom", unit = "phòng", autoAttach = false,
            initialPrice = new { effectiveFrom = start, unitPrice = 10000 }
        }, token)).ReadIdAsync();
        var periods = await (await _client.GetAsync($"/api/v1/contracts/{contractId}/billing-periods", token)).ReadAsync<List<JsonElement>>();
        var nextPeriod = DateOnly.Parse(periods[1].GetProperty("start").GetString()!);
        (await _client.PutJsonAsync($"/api/v1/contracts/{contractId}/fees/{garbage}", new { unitPriceOverride = 18000, effectiveFrom = nextPeriod }, token))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);
        var roommate = await _client.CreateRenterAsync(token, phone: null);
        await _client.PostJsonAsync($"/api/v1/contracts/{contractId}/occupants", new { renterId = roommate, moveInDate = start, relationshipType = "CoTenant" }, token);
        var detail = await (await _client.GetAsync($"/api/v1/contracts/{contractId}", token)).ReadAsync<JsonElement>();
        var roommateOccupancy = detail.GetProperty("occupants").EnumerateArray().Single(o => o.GetProperty("renterId").GetGuid() == roommate).GetProperty("id").GetGuid();
        await _client.PostJsonAsync($"/api/v1/contracts/{contractId}/occupants/{roommateOccupancy}/end", new { moveOutDate = today.AddDays(-1) }, token);

        var text = await DocumentTextAsync(token, contractId);

        text.Should().Contain("Nước: theo chỉ số công tơ của phòng (tính từ chỉ số ngày nhận phòng), 15.500,5 đ/m³",
            "in đúng giá đã thỏa thuận lúc ký, không làm tròn");
        text.Should().NotContain("30.000 đ/m³", "giá mới của khu áp dụng sau khi ký không thay đổi văn bản đã ký");
        text.Should().Contain($"Từ ngày {nextPeriod:dd/MM/yyyy}: Rác: 18.000 đ/phòng/tháng.");
        text.Should().NotContain("Những người cùng ở", "người ở cùng đã chuyển đi");
        renterId.Should().NotBeEmpty();
    }

    [Fact]
    public async Task Activation_CapturesUtilityPriceSnapshot()
    {
        var owner = await _client.CreateActiveOwnerAsync();
        var token = owner.Tokens.AccessToken;
        var today = TestData.Today(factory);
        var propertyId = await _client.CreatePropertyAsync(token);
        var fees = await (await _client.GetAsync($"/api/v1/properties/{propertyId}/fee-types", token)).ReadAsync<List<JsonElement>>();
        var electricity = fees.Single(f => f.GetProperty("name").GetString() == "Điện").GetProperty("id").GetGuid();
        await _client.PostJsonAsync($"/api/v1/fee-types/{electricity}/prices", new { effectiveFrom = today, unitPrice = 3500 }, token);
        var roomId = await _client.CreateRoomAsync(token, propertyId);
        var meters = new[]
        {
            await _client.InstallMeterAsync(token, roomId, electricity, today),
            await _client.InstallMeterAsync(token, roomId, await _client.FeeIdAsync(token, propertyId, "Nước"), today)
        };
        var contractId = await _client.CreateContractAsync(token, roomId, await _client.CreateRenterAsync(token), today);
        (await _client.PostJsonAsync($"/api/v1/contracts/{contractId}/activate",
            new { handoverReadings = meters.Select(m => new { meterId = m, value = (decimal?)null }) }, token)).StatusCode.Should().Be(HttpStatusCode.NoContent);

        var snapshot = (await (await _client.GetAsync($"/api/v1/contracts/{contractId}", token)).ReadAsync<JsonElement>()).GetProperty("utilityPrices");

        snapshot.EnumerateArray().Select(f => (f.GetProperty("name").GetString(), f.GetProperty("unitPrice").ValueKind == JsonValueKind.Null ? (decimal?)null : f.GetProperty("unitPrice").GetDecimal()))
            .Should().Equal(("Điện", 3500m), ("Nước", (decimal?)null));
    }

    [Fact]
    public async Task Draft_IsMarkedAsDraft()
    {
        var owner = await _client.CreateActiveOwnerAsync();
        var token = owner.Tokens.AccessToken;
        var roomId = await _client.CreateRoomAsync(token, await _client.CreatePropertyAsync(token));
        var contractId = await _client.CreateContractAsync(token, roomId, await _client.CreateRenterAsync(token), TestData.Today(factory));

        (await DocumentTextAsync(token, contractId)).Should().Contain("BẢN NHÁP");
    }
}
