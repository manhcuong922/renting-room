using System.Net;
using ClosedXML.Excel;
using renting_room.IntegrationTests.Infrastructure;

namespace renting_room.IntegrationTests;

[Collection(ApiCollection.Name)]
public sealed class ExportRentersTests(ApiFactory factory)
{
    private const string ExportUrl = "/api/v1/exports/renters";
    private const int HeaderRow = 4;
    private readonly HttpClient _client = factory.CreateClient();

    /// <summary>Khu A: P101 (tầng 1, 2 người), P201 (tầng 2, thuộc nhóm "Ban công"); khu B: B01 (1 người).</summary>
    private sealed record Fixture(string Token, Guid PropertyA, Guid PropertyB, Guid GroupId, string RepresentativeIdNumber);

    private async Task<Fixture> SeedAsync()
    {
        var owner = await _client.CreateActiveOwnerAsync();
        var token = owner.Tokens.AccessToken;
        var today = TestData.Today(factory);
        var propertyA = await CreatePropertyAsync(token, "Khu Quang Minh");
        var propertyB = await CreatePropertyAsync(token, "Khu Cầu Giấy");

        var idNumber = TestData.NewCitizenId();
        var lan = await CreateRenterAsync(token, "Trần Thị Lan", idNumber);
        var hoa = await CreateRenterAsync(token, "=HYPERLINK(\"http://x\")", TestData.NewCitizenId());
        await ActivateAsync(token, await CreateRoomAsync(token, propertyA, "101", "1"), lan, today, lan, hoa);
        var room201 = await CreateRoomAsync(token, propertyA, "201", "2");
        await ActivateAsync(token, room201, await CreateRenterAsync(token, "Phạm Thị Huệ", TestData.NewCitizenId()), today);
        await ActivateAsync(token, await CreateRoomAsync(token, propertyB, "B01", "1"),
            await CreateRenterAsync(token, "Lê Văn Nam", TestData.NewCitizenId()), today);

        var group = await (await _client.PostJsonAsync($"/api/v1/properties/{propertyA}/room-groups", new { name = "Ban công" }, token)).ReadIdAsync();
        await _client.PutJsonAsync($"/api/v1/room-groups/{group}/members", new { roomIds = new[] { room201 } }, token);
        return new Fixture(token, propertyA, propertyB, group, idNumber);
    }

    private async Task<Guid> CreatePropertyAsync(string token, string name)
    {
        var id = await _client.CreatePropertyAsync(token);
        var detail = await (await _client.GetAsync($"/api/v1/properties/{id}", token)).ReadAsync<System.Text.Json.JsonElement>();
        await _client.PutJsonAsync($"/api/v1/properties/{id}", new
        {
            name, address = TestData.Address(),
            version = detail.GetProperty("version").GetString()
        }, token);
        return id;
    }

    private async Task<Guid> CreateRoomAsync(string token, Guid propertyId, string code, string floor) =>
        await (await _client.PostJsonAsync($"/api/v1/properties/{propertyId}/rooms",
            new { code, spec = new { floor, maxOccupants = 3, listedRent = 1_000_000 } }, token)).ReadIdAsync();

    private async Task<Guid> CreateRenterAsync(string token, string fullName, string idNumber) =>
        await (await _client.PostJsonAsync("/api/v1/renters", new
        {
            fullName, dateOfBirth = "1995-03-10", gender = "Female", phone = "0367733904", idType = "CitizenId", idNumber,
            permanentAddress = "Xóm Hồng Sơn, Nghĩa Phúc, Nghệ An"
        }, token)).ReadIdAsync();

    private async Task ActivateAsync(string token, Guid roomId, Guid representativeId, DateOnly start, params Guid[] occupants)
    {
        var contractId = await _client.CreateContractAsync(token, roomId, representativeId, start, occupants);
        (await _client.PostJsonAsync($"/api/v1/contracts/{contractId}/activate", null, token)).StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    private async Task<XLWorkbook> ExportAsync(string token, object filter)
    {
        var response = await _client.PostJsonAsync(ExportUrl, filter, token, idempotencyKey: null);
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
        response.Content.Headers.ContentDisposition!.FileName.Should().MatchRegex(@"danh-sach-nguoi-thue_\d{8}-\d{4}\.xlsx");
        return new XLWorkbook(await response.Content.ReadAsStreamAsync());
    }

    private static List<string> Names(IXLWorksheet ws) =>
        ws.RowsUsed().Where(r => r.RowNumber() > HeaderRow).Select(r => r.Cell(7).GetString()).ToList();

    [Fact]
    public async Task SheetPerProperty_ListsOccupantsOfEachProperty_WithMaskedIdNumbers()
    {
        var f = await SeedAsync();

        using var book = await ExportAsync(f.Token, new { });

        book.Worksheets.Select(w => w.Name).Should().BeEquivalentTo("Khu Quang Minh", "Khu Cầu Giấy");
        var sheetA = book.Worksheet("Khu Quang Minh");
        sheetA.Cell(HeaderRow, 7).GetString().Should().Be("Họ tên");
        Names(sheetA).Should().Equal("Trần Thị Lan", "=HYPERLINK(\"http://x\")", "Phạm Thị Huệ");
        sheetA.Cell(HeaderRow + 1, 6).GetString().Should().Be("Đại diện");
        sheetA.Cell(HeaderRow + 1, 10).GetString().Should().Be("0367733904", "SĐT giữ số 0 đầu");
        sheetA.Cell(HeaderRow + 1, 12).GetString().Should().Be($"********{f.RepresentativeIdNumber[^4..]}");
        sheetA.Cell(HeaderRow + 1, 8).DataType.Should().Be(XLDataType.DateTime);

        var injected = sheetA.Cell(HeaderRow + 2, 7);
        injected.HasFormula.Should().BeFalse("chuỗi người dùng nhập không bao giờ thành công thức");
        injected.Style.IncludeQuotePrefix.Should().BeTrue();
    }

    [Fact]
    public async Task Filters_ByProperty_Floor_AndRoomGroup()
    {
        var f = await SeedAsync();

        using var onlyA = await ExportAsync(f.Token, new { propertyIds = new[] { f.PropertyA }, layout = "SingleSheet" });
        Names(onlyA.Worksheets.Single()).Should().HaveCount(3);

        using var floor2 = await ExportAsync(f.Token, new { propertyIds = new[] { f.PropertyA }, floors = new[] { "2" } });
        Names(floor2.Worksheets.Single()).Should().Equal("Phạm Thị Huệ");

        using var group = await ExportAsync(f.Token, new { roomGroupIds = new[] { f.GroupId } });
        Names(group.Worksheets.Single()).Should().Equal("Phạm Thị Huệ");

        using var perFloor = await ExportAsync(f.Token, new { propertyIds = new[] { f.PropertyA }, layout = "SheetPerFloor" });
        perFloor.Worksheets.Select(w => w.Name).Should().HaveCount(2).And.OnlyContain(n => n.EndsWith("Tầng 1") || n.EndsWith("Tầng 2"));
    }

    [Fact]
    public async Task IncludeSensitive_ExportsFullIdNumbers()
    {
        var f = await SeedAsync();

        using var book = await ExportAsync(f.Token, new { propertyIds = new[] { f.PropertyA }, includeSensitive = true });

        book.Worksheets.Single().Cell(HeaderRow + 1, 12).GetString().Should().Be(f.RepresentativeIdNumber);
    }

    [Fact]
    public async Task LiquidatingContract_OccupantsLeaveOnAgreedEndDate()
    {
        var owner = await _client.CreateActiveOwnerAsync();
        var token = owner.Tokens.AccessToken;
        var today = TestData.Today(factory);
        var (propertyId, _, _, contractId) = await _client.CreateActiveContractAsync(token, today);
        await _client.PostJsonAsync($"/api/v1/contracts/{contractId}/liquidation/start",
            new { actualEndDate = today.AddDays(5), reason = "MutualAgreement" }, token);

        using var before = await ExportAsync(token, new { propertyIds = new[] { propertyId }, fromDate = today.AddDays(5), toDate = today.AddDays(5) });
        Names(before.Worksheets.Single()).Should().HaveCount(1, "vẫn ở trong ngày trả phòng");
        before.Worksheets.Single().Cell(HeaderRow + 1, 22).GetDateTime().Should().Be(today.AddDays(5).ToDateTime(TimeOnly.MinValue));

        using var after = await ExportAsync(token, new { propertyIds = new[] { propertyId }, fromDate = today.AddDays(6), toDate = today.AddDays(6) });
        Names(after.Worksheets.Single()).Should().BeEmpty();
    }

    [Fact]
    public async Task DateBeforeMoveIn_GivesEmptySheet_AndForeignPropertyIs404()
    {
        var f = await SeedAsync();
        var other = await _client.CreateActiveOwnerAsync();
        var yesterday = TestData.Today(factory).AddDays(-1);

        using var book = await ExportAsync(f.Token, new { fromDate = yesterday, toDate = yesterday });
        Names(book.Worksheets.Single()).Should().BeEmpty();

        var foreign = await _client.PostJsonAsync(ExportUrl, new { propertyIds = new[] { f.PropertyA } }, other.Tokens.AccessToken, idempotencyKey: null);
        foreign.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await foreign.ReadProblemCodeAsync()).Should().Be("PROPERTY_NOT_FOUND");

        var badRange = await _client.PostJsonAsync(ExportUrl, new { fromDate = yesterday, toDate = yesterday.AddDays(-1) }, f.Token, idempotencyKey: null);
        (await badRange.ReadProblemCodeAsync()).Should().Be("VALIDATION_FAILED");
    }
}
