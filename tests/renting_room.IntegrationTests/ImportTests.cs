using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using ClosedXML.Excel;
using renting_room.IntegrationTests.Infrastructure;

namespace renting_room.IntegrationTests;

/// <summary>
/// PR-UC-11 (import phòng), RT-UC-10 (import người thuê đang ở): xem trước (server không giữ gì) → gửi lại các dòng → lưu phần hợp lệ, kết quả
/// từng đơn vị (dòng phòng / phòng). Import chỉ tạo mới.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class ImportTests(ApiFactory factory)
{
    private readonly HttpClient _client = factory.CreateClient();

    private static byte[] Workbook(string sheet, params object?[][] rows)
    {
        using var workbook = new XLWorkbook();
        var ws = workbook.Worksheets.Add(sheet);
        for (var r = 0; r < rows.Length; r++)
            for (var c = 0; c < rows[r].Length; c++)
            {
                var cell = ws.Cell(r + 1, c + 1);
                switch (rows[r][c])
                {
                    case null: break;
                    case DateOnly d: cell.Value = d.ToDateTime(TimeOnly.MinValue); break;
                    case int i: cell.Value = i; break;
                    case decimal m: cell.Value = m; break;
                    case string s when s.StartsWith('='): cell.FormulaA1 = s[1..]; break;
                    case var v: cell.Value = v.ToString(); break;
                }
            }
        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    private async Task<HttpResponseMessage> UploadAsync(string url, byte[] content, string token)
    {
        using var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(content);
        file.Headers.ContentType = new MediaTypeHeaderValue("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
        form.Add(file, "file", "import.xlsx");
        using var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = form };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await _client.SendAsync(request);
    }

    private async Task<JsonElement> PreviewAsync(string url, byte[] content, string token)
    {
        var response = await UploadAsync(url, content, token);
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return await response.ReadAsync<JsonElement>();
    }

    private static IEnumerable<string?> Codes(JsonElement issues) => issues.EnumerateArray().Select(e => e.GetProperty("code").GetString());

    private static object Rows(JsonElement preview) => new { rows = preview.GetProperty("rows").EnumerateArray().Select(r => r.GetProperty("row")).ToList() };

    private static readonly object?[] RoomHeader =
    [
        "Mã phòng*", "Tầng", "Diện tích (m²)", "Số người (loại phòng)", "Giá niêm yết", "Tiền cọc mặc định", "Tiện nghi", "Ghi chú",
        "Số seri công tơ điện", "Chỉ số điện đầu kỳ", "Số seri công tơ nước", "Chỉ số nước đầu kỳ"
    ];

    private static readonly object?[] TenancyHeader =
    [
        "Mã phòng*", "Vai trò*", "Họ tên*", "Ngày sinh*", "Giới tính*", "SĐT", "Loại giấy tờ", "Số giấy tờ", "Quốc tịch", "Quê quán / thường trú",
        "Nghề nghiệp", "Quan hệ với người đứng tên", "Ngày vào ở*", "Giá thuê", "Tiền cọc"
    ];

    [Fact]
    public async Task RoomImport_SavesValidRows_SkipsInvalid_MeterInstalledFromCurrentPeriodStart()
    {
        var owner = await _client.CreateActiveOwnerAsync();
        var token = owner.Tokens.AccessToken;
        var today = TestData.Today(factory);
        var propertyId = await _client.CreatePropertyAsync(token, anchorDay: 1);
        await _client.CreateRoomAsync(token, propertyId, "301");
        (await _client.GetAsync($"/api/v1/properties/{propertyId}/rooms/import-template", token)).StatusCode.Should().Be(HttpStatusCode.OK);

        var preview = await PreviewAsync($"/api/v1/properties/{propertyId}/rooms/import/preview", Workbook("Phòng",
            ["Mẫu nhập phòng"], [], RoomHeader,
            ["101", "1", 20, null, 3_000_000, 3_000_000, "air_con, wifi", null, "E-101", 1250, "W-101", 340],
            ["102", "1", 18, 5, 2_800_000, null, null, null, null, null, null, null],
            ["102", "1", 18, 2, 2_800_000, null, null, null, null, null, null, null],
            ["301", "3", 20, 2, 3_000_000, null, null, null, null, null, null, null],
            ["103", "1", 18, "hai", 2_800_000, null, null, null, null, null, null, null]), token);
        preview.GetProperty("validCount").GetInt32().Should().Be(1);
        preview.GetProperty("errorCount").GetInt32().Should().Be(4);
        var issues = preview.GetProperty("rows").EnumerateArray().SelectMany(r => Codes(r.GetProperty("errors"))).ToList();
        issues.Should().Contain(["DUPLICATE_IN_FILE", "ROOM_CODE_TAKEN", "INVALID_NUMBER"]);
        preview.GetProperty("rows")[0].GetProperty("row").GetProperty("maxOccupants").ValueKind.Should().Be(JsonValueKind.Null, "số người không bắt buộc");

        // Người dùng sửa thẳng trên màn: bỏ dòng 102 trùng, sửa số người phòng 103 — rồi gửi lại.
        var rows = preview.GetProperty("rows").EnumerateArray().Select(r => r.GetProperty("row")).ToList();
        var edited = new List<object>
        {
            rows[0], rows[1],
            JsonSerializer.Deserialize<Dictionary<string, object?>>(rows[4].GetRawText())! is var room103
                ? room103.Concat([new("maxOccupants", 2)]).GroupBy(p => p.Key).ToDictionary(g => g.Key, g => g.Last().Value) : null!,
            rows[3]
        };
        var saved = await (await _client.PostJsonAsync($"/api/v1/properties/{propertyId}/rooms/import", new { rows = edited }, token)).ReadAsync<JsonElement>();

        saved.GetProperty("saved").GetInt32().Should().Be(3);
        saved.GetProperty("skipped").GetInt32().Should().Be(1);
        saved.GetProperty("units").EnumerateArray().Single(u => u.GetProperty("key").GetString() == "301")
            .GetProperty("outcome").GetString().Should().Be("Invalid");
        var list = await (await _client.GetAsync($"/api/v1/rooms?propertyId={propertyId}&pageSize=50", token)).ReadAsync<JsonElement>();
        var room101 = list.GetProperty("items").EnumerateArray().Single(r => r.GetProperty("code").GetString() == "101").GetProperty("id").GetGuid();
        var meters = await (await _client.GetAsync($"/api/v1/rooms/{room101}/meters", token)).ReadAsync<List<JsonElement>>();
        meters.Should().HaveCount(2).And.OnlyContain(m => m.GetProperty("installedDate").GetString() == $"{new DateOnly(today.Year, today.Month, 1):yyyy-MM-dd}");
    }

    [Fact]
    public async Task TenancyImport_FamilyChildWithoutId_OnePersonRentsTwoRooms_InvalidRoomsSkipped()
    {
        var owner = await _client.CreateActiveOwnerAsync();
        var token = owner.Tokens.AccessToken;
        var today = TestData.Today(factory);
        var monthStart = new DateOnly(today.Year, today.Month, 1);
        var propertyId = await _client.CreatePropertyAsync(token, anchorDay: 1);
        var room101 = await _client.CreateRoomAsync(token, propertyId, "101");
        var room102 = await _client.CreateRoomAsync(token, propertyId, "102");
        var room103 = await _client.CreateRoomAsync(token, propertyId, "103");
        var busy = await _client.CreateContractAsync(token, room103, await _client.CreateRenterAsync(token), today);
        (await _client.PostJsonAsync($"/api/v1/contracts/{busy}/activate", null, token)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        var wifeId = TestData.NewCitizenId();
        var existingWife = await _client.CreateRenterAsync(token, idNumber: wifeId);
        var husbandId = TestData.NewCitizenId();
        var studentId = TestData.NewCitizenId();
        var url = $"/api/v1/properties/{propertyId}/tenancies/import";

        var preview = await PreviewAsync($"{url}/preview", Workbook("Người thuê",
            TenancyHeader,
            ["101", "Đứng tên", "Phạm Văn Hùng", new DateOnly(1985, 4, 2), "Nam", "0912345678", "CCCD", husbandId, "VN", "Nam Định", null, null,
                today.AddYears(-2), 3_500_000, 3_500_000],
            ["101", "Ở cùng", "Trần Thị Lan", new DateOnly(2000, 4, 15), "Nữ", null, "CCCD", wifeId, "VN", null, null, "Vợ", today.AddYears(-2), null, null],
            ["101", "Ở cùng", "Phạm Minh An", today.AddYears(-7), "Nam", null, null, null, "VN", null, "Học sinh", "Con", today.AddYears(-2), null, null],
            ["102", "Đứng tên (không ở)", "Phạm Văn Hùng", new DateOnly(1985, 4, 2), "Nam", "0912345678", "CCCD", husbandId, "VN", null, null, null,
                today.AddMonths(-3), 2_500_000, 0],
            ["102", "Ở cùng", "Đào Thị Nga", new DateOnly(2004, 5, 5), "Nữ", null, "CCCD", studentId, "VN", null, "Sinh viên", "Cùng ở thuê",
                today.AddMonths(-3), null, null],
            ["103", "Đứng tên", "Lý Văn Mạnh", new DateOnly(1995, 3, 3), "Nam", null, "CCCD", TestData.NewCitizenId(), "VN", null, null, null,
                today.AddMonths(-1), 2_000_000, 0],
            ["109", "Đứng tên", "Hồ Văn Tư", new DateOnly(1990, 1, 1), "Nam", null, "CCCD", TestData.NewCitizenId(), "VN", null, null, null,
                today.AddMonths(-1), 2_000_000, 0],
            ["104", "Đứng tên", "Vũ Thị Hoa", new DateOnly(1990, 1, 1), "Nữ", null, null, null, "VN", null, null, null, today.AddMonths(-1), 2_000_000, 0]),
            token);

        preview.GetProperty("validRooms").GetInt32().Should().Be(2);
        preview.GetProperty("invalidRooms").GetInt32().Should().Be(3);
        var rooms = preview.GetProperty("rooms").EnumerateArray().ToDictionary(r => r.GetProperty("roomCode").GetString()!);
        Codes(rooms["103"].GetProperty("errors")).Should().Contain("ROOM_HAS_CONTRACTS");
        Codes(rooms["109"].GetProperty("errors")).Should().Contain("ROOM_NOT_FOUND");
        rooms["101"].GetProperty("billingStartDate").GetString().Should().Be($"{monthStart:yyyy-MM-dd}", "tính tiền từ đầu kỳ hiện tại của khu");
        var rows = preview.GetProperty("rows").EnumerateArray().ToList();
        Codes(rows[1].GetProperty("warnings")).Should().Contain("RENTER_EXISTS");
        rows[2].GetProperty("errors").GetArrayLength().Should().Be(0, "trẻ dưới 14 tuổi không cần giấy tờ");
        Codes(rows[7].GetProperty("errors")).Should().Contain("REPRESENTATIVE_ID_REQUIRED");

        var saved = await (await _client.PostJsonAsync(url, Rows(preview), token)).ReadAsync<JsonElement>();
        saved.GetProperty("saved").GetInt32().Should().Be(2);
        saved.GetProperty("skipped").GetInt32().Should().Be(3);

        var contract101 = (await (await _client.GetAsync($"/api/v1/rooms/{room101}", token)).ReadAsync<JsonElement>()).GetProperty("currentContract").GetProperty("id").GetGuid();
        var detail101 = await (await _client.GetAsync($"/api/v1/contracts/{contract101}", token)).ReadAsync<JsonElement>();
        detail101.GetProperty("status").GetString().Should().Be("Active");
        detail101.GetProperty("occupants").GetArrayLength().Should().Be(3);
        detail101.GetProperty("hasSignedDocument").GetBoolean().Should().BeFalse("HĐ import mặc định thiếu tài liệu");
        detail101.GetProperty("occupants").EnumerateArray().Select(o => o.GetProperty("renterId").GetGuid()).Should().Contain(existingWife);
        var contract102 = (await (await _client.GetAsync($"/api/v1/rooms/{room102}", token)).ReadAsync<JsonElement>()).GetProperty("currentContract").GetProperty("id").GetGuid();
        var detail102 = await (await _client.GetAsync($"/api/v1/contracts/{contract102}", token)).ReadAsync<JsonElement>();
        detail102.GetProperty("representativeRenterId").GetGuid().Should().Be(detail101.GetProperty("representativeRenterId").GetGuid(),
            "một người đứng tên 2 phòng dùng chung 1 hồ sơ");
        detail102.GetProperty("occupants").GetArrayLength().Should().Be(1, "người đứng tên không ở phòng 102");

        // Gửi lại lần 2 không nhân đôi: phòng đã có HĐ ⇒ bỏ qua.
        var again = await (await _client.PostJsonAsync(url, Rows(preview), token)).ReadAsync<JsonElement>();
        again.GetProperty("saved").GetInt32().Should().Be(0);
    }

    [Fact]
    public async Task Import_RejectsNonXlsx_ZipBomb_AndFormulaCells()
    {
        var owner = await _client.CreateActiveOwnerAsync();
        var token = owner.Tokens.AccessToken;
        var propertyId = await _client.CreatePropertyAsync(token);
        var url = $"/api/v1/properties/{propertyId}/rooms/import/preview";

        (await (await UploadAsync(url, "không phải excel"u8.ToArray(), token)).ReadProblemCodeAsync()).Should().Be("IMPORT_FILE_INVALID");

        using var bomb = new MemoryStream();
        using (var zip = new ZipArchive(bomb, ZipArchiveMode.Create, leaveOpen: true))
        {
            await using var entry = zip.CreateEntry("xl/workbook.xml", CompressionLevel.SmallestSize).Open();
            var zeros = new byte[1024 * 1024];
            for (var i = 0; i < 21; i++)
                await entry.WriteAsync(zeros);
        }
        bomb.Length.Should().BeLessThan(1024 * 1024, "file nén nhỏ nhưng giải nén > 20 MB");
        (await (await UploadAsync(url, bomb.ToArray(), token)).ReadProblemCodeAsync()).Should().Be("IMPORT_FILE_TOO_LARGE");

        var formula = await PreviewAsync(url, Workbook("Phòng", RoomHeader, ["101", "1", 20, 2, "=1+1", null, null, null, null, null, null, null]), token);
        Codes(formula.GetProperty("rows")[0].GetProperty("errors")).Should().Contain("FORMULA_NOT_ALLOWED");
    }
}
