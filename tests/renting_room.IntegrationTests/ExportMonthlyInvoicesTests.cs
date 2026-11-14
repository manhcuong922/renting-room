using System.Net;
using System.Text.Json;
using ClosedXML.Excel;
using renting_room.IntegrationTests.Infrastructure;

namespace renting_room.IntegrationTests;

/// <summary>
/// M10 E2 "Thu tiền theo kỳ" (09/11/2026): mỗi hóa đơn 1 dòng — tổng, đã thu, bỏ nợ (lý do, người), sửa giảm đã thu (lý do, người), còn nợ,
/// đơn giá điện nước; sheet tổng theo (kỳ, khu). Kịch bản X1–X5 ở plan 10 §12.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class ExportMonthlyInvoicesTests(ApiFactory factory)
{
    private const string ExportUrl = "/api/v1/exports/monthly-invoices";
    private const int HeaderRow = 4;
    private const decimal Rent = 3_000_000;
    private readonly HttpClient _client = factory.CreateClient();

    private static readonly string[] DetailHeaders =
    [
        "STT", "Kỳ", "Khu", "Phòng", "Số hóa đơn", "Loại", "Người đứng tên", "Trạng thái", "Tiền phòng", "Điện nước", "Dịch vụ",
        "Phụ thu − giảm trừ − hoàn trả", "Kết toán", "Tổng", "Đã thu", "Ngày thu gần nhất", "Bỏ nợ", "Lý do bỏ nợ", "Người bỏ nợ",
        "Sửa giảm", "Lý do sửa giảm", "Người sửa", "Còn nợ", "Đơn giá điện", "Đơn giá nước"
    ];

    private sealed record Room(Guid ContractId, Guid PreviousInvoiceId, Guid CurrentInvoiceId);

    private sealed record Seed(string Token, Guid PropertyA, Guid PropertyB, Room P101, Room P102, DateOnly Previous, DateOnly Month, DateOnly Today);

    /// <summary>Khu A (thu trước, chốt ngày 1): P101, P102 từ đầu tháng trước, 2 hóa đơn 3tr / phòng đã chốt; khu B: 1 phòng, 1 hóa đơn nháp.</summary>
    private async Task<Seed> SeedAsync()
    {
        var owner = await _client.CreateActiveOwnerAsync();
        var token = owner.Tokens.AccessToken;
        var today = TestData.Today(factory);
        var month = new DateOnly(today.Year, today.Month, 1);
        var previous = month.AddMonths(-1);
        var propertyA = await _client.CreatePropertyAsync(token, anchorDay: 1, chargeMode: "Prepaid");
        var c101 = await ContractAsync(token, propertyA, "P101", previous);
        var c102 = await ContractAsync(token, propertyA, "P102", previous);
        await GenerateAsync(token, propertyA, previous);
        await GenerateAsync(token, propertyA, month);
        var p101 = new Room(c101, await FinalizeAsync(token, c101, previous), await FinalizeAsync(token, c101, month));
        var p102 = new Room(c102, await FinalizeAsync(token, c102, previous), await FinalizeAsync(token, c102, month));

        var propertyB = await _client.CreatePropertyAsync(token, anchorDay: 1, chargeMode: "Prepaid");
        await ContractAsync(token, propertyB, "B01", month);
        await GenerateAsync(token, propertyB, month);
        return new Seed(token, propertyA, propertyB, p101, p102, previous, month, today);
    }

    private async Task<Guid> ContractAsync(string token, Guid propertyId, string code, DateOnly start)
    {
        var roomId = await _client.CreateRoomAsync(token, propertyId, code);
        var renter = await _client.CreateRenterAsync(token);
        var contractId = await (await _client.PostJsonAsync("/api/v1/contracts", new
        {
            roomId,
            contract = new { representativeRenterId = renter, startDate = start, monthlyRent = Rent, depositAmount = 0, occupants = new[] { new { renterId = renter } } }
        }, token)).ReadIdAsync();
        (await _client.PostJsonAsync($"/api/v1/contracts/{contractId}/activate", null, token)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        return contractId;
    }

    private async Task GenerateAsync(string token, Guid propertyId, DateOnly month) =>
        (await _client.PostJsonAsync("/api/v1/invoices/generate", new { propertyId, billingMonth = $"{month:yyyy-MM}" }, token)).StatusCode
        .Should().Be(HttpStatusCode.OK);

    private async Task<Guid> FinalizeAsync(string token, Guid contractId, DateOnly month)
    {
        var page = await (await _client.GetAsync($"/api/v1/invoices?contractId={contractId}&billingMonth={month:yyyy-MM}", token)).ReadAsync<JsonElement>();
        var id = page.GetProperty("items")[0].GetProperty("id").GetGuid();
        (await _client.FinalizeInvoiceAsync(id, token)).StatusCode.Should().Be(HttpStatusCode.OK);
        return id;
    }

    private async Task PayAsync(string token, Guid contractId, Guid invoiceId, decimal amount, DateOnly paidAt) =>
        (await _client.PostJsonAsync($"/api/v1/contracts/{contractId}/payments", new { amount, method = "Cash", paidAt, invoiceId }, token))
        .StatusCode.Should().Be(HttpStatusCode.Created);

    private async Task<XLWorkbook> ExportAsync(string token, object filter)
    {
        var response = await _client.PostJsonAsync(ExportUrl, filter, token, idempotencyKey: null);
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        response.Content.Headers.ContentDisposition!.FileName.Should().MatchRegex(@"thu-tien-theo-ky_\d{8}-\d{4}\.xlsx");
        return new XLWorkbook(await response.Content.ReadAsStreamAsync());
    }

    private static List<Dictionary<string, IXLCell>> Rows(IXLWorksheet ws)
    {
        var headers = ws.Row(HeaderRow).CellsUsed().ToDictionary(c => c.GetString(), c => c.Address.ColumnNumber);
        return ws.RowsUsed().Where(r => r.RowNumber() > HeaderRow)
            .Select(r => headers.ToDictionary(h => h.Key, h => r.Cell(h.Value)))
            .ToList();
    }

    [Fact]
    public async Task X1_DetailAndSummary_ShowCollected_WriteOff_Reduction_WithReasonsAndPeople()
    {
        var f = await SeedAsync();
        await PayAsync(f.Token, f.P101.ContractId, f.P101.PreviousInvoiceId, Rent, f.Today);
        (await _client.PostJsonAsync($"/api/v1/invoices/{f.P102.PreviousInvoiceId}/pay-and-write-off",
                new { amount = 2_000_000, method = "Cash", paidAt = f.Today, reason = "Khó khăn" }, f.Token))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        await PayAsync(f.Token, f.P101.ContractId, f.P101.CurrentInvoiceId, Rent, f.Today);
        (await _client.PutJsonAsync($"/api/v1/invoices/{f.P101.CurrentInvoiceId}/collected",
                new { amount = 2_800_000, expectedAmount = Rent, reason = "Nhập nhầm" }, f.Token))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        using var book = await ExportAsync(f.Token, new { propertyIds = new[] { f.PropertyA }, fromMonth = $"{f.Previous:yyyy-MM}", toMonth = $"{f.Month:yyyy-MM}" });

        book.Worksheets.Select(w => w.Name).Should().Equal("Chi tiết", "Tổng theo kỳ");
        var detail = book.Worksheet("Chi tiết");
        detail.Row(HeaderRow).CellsUsed().Select(c => c.GetString()).Should().Equal(DetailHeaders);
        var rows = Rows(detail);
        rows.Select(r => (r["Kỳ"].GetString(), r["Phòng"].GetString())).Should().Equal(
            ($"{f.Previous:MM/yyyy}", "P101"), ($"{f.Previous:MM/yyyy}", "P102"), ($"{f.Month:MM/yyyy}", "P101"), ($"{f.Month:MM/yyyy}", "P102"));

        var p102Previous = rows[1];
        p102Previous["Tổng"].GetValue<decimal>().Should().Be(Rent);
        p102Previous["Đã thu"].GetValue<decimal>().Should().Be(2_000_000);
        p102Previous["Bỏ nợ"].GetValue<decimal>().Should().Be(1_000_000);
        p102Previous["Lý do bỏ nợ"].GetString().Should().Be("Khó khăn");
        p102Previous["Người bỏ nợ"].GetString().Should().NotBeNullOrWhiteSpace();
        p102Previous["Còn nợ"].GetValue<decimal>().Should().Be(0);
        p102Previous["Trạng thái"].GetString().Should().Be("Đã bỏ nợ");

        var p101Current = rows[2];
        p101Current["Đã thu"].GetValue<decimal>().Should().Be(2_800_000);
        p101Current["Sửa giảm"].GetValue<decimal>().Should().Be(200_000);
        p101Current["Lý do sửa giảm"].GetString().Should().Be("Nhập nhầm");
        p101Current["Người sửa"].GetString().Should().NotBeNullOrWhiteSpace();
        p101Current["Còn nợ"].GetValue<decimal>().Should().Be(200_000);
        p101Current["Ngày thu gần nhất"].GetDateTime().Should().Be(f.Today.ToDateTime(TimeOnly.MinValue));
        p101Current["Tiền phòng"].GetValue<decimal>().Should().Be(Rent);
        p101Current["Trạng thái"].GetString().Should().Be("Thu một phần");

        rows[3]["Đã thu"].GetValue<decimal>().Should().Be(0);
        rows[3]["Còn nợ"].GetValue<decimal>().Should().Be(Rent);

        var summary = Rows(book.Worksheet("Tổng theo kỳ"));
        summary.Should().HaveCount(3, "2 kỳ + dòng tổng cộng");
        summary[0]["Đã thu"].GetValue<decimal>().Should().Be(5_000_000);
        summary[0]["Bỏ nợ"].GetValue<decimal>().Should().Be(1_000_000);
        summary[1]["Sửa giảm"].GetValue<decimal>().Should().Be(200_000);
        summary[1]["Còn nợ"].GetValue<decimal>().Should().Be(3_200_000);
        summary[2]["Kỳ"].GetString().Should().Be("Tổng cộng");
        summary[2]["Tổng"].GetValue<decimal>().Should().Be(4 * Rent);
        summary[2]["Đã thu"].GetValue<decimal>().Should().Be(7_800_000);
    }

    [Fact]
    public async Task X2_DraftsOnlyWhenAsked_X3_Validation()
    {
        var f = await SeedAsync();
        var range = new { fromMonth = $"{f.Month:yyyy-MM}", toMonth = $"{f.Month:yyyy-MM}" };

        using var withoutDrafts = await ExportAsync(f.Token, new { propertyIds = new[] { f.PropertyB }, range.fromMonth, range.toMonth });
        Rows(withoutDrafts.Worksheet("Chi tiết")).Should().BeEmpty();
        using var withDrafts = await ExportAsync(f.Token, new { propertyIds = new[] { f.PropertyB }, range.fromMonth, range.toMonth, includeDrafts = true });
        Rows(withDrafts.Worksheet("Chi tiết")).Single()["Trạng thái"].GetString().Should().Be("Nháp");

        (await _client.PostJsonAsync(ExportUrl, new { fromMonth = $"{f.Month:yyyy-MM}", toMonth = $"{f.Previous:yyyy-MM}" }, f.Token, idempotencyKey: null))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await _client.PostJsonAsync(ExportUrl, new { fromMonth = $"{f.Month.AddMonths(-24):yyyy-MM}", toMonth = $"{f.Month:yyyy-MM}" }, f.Token, idempotencyKey: null))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await _client.PostJsonAsync(ExportUrl, new { propertyIds = new[] { Guid.NewGuid() }, range.fromMonth, range.toMonth }, f.Token, idempotencyKey: null))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task X4_Manager_OnlyAssignedProperty()
    {
        var f = await SeedAsync();
        var created = await _client.PostJsonAsync("/api/v1/org/members",
            new { fullName = "Quản Lý Khu A", phone = TestApi.NewPhone(), propertyIds = new[] { f.PropertyA } }, f.Token);
        var credentials = await created.ReadAsync<TemporaryCredentialsResponse>();
        var login = await _client.LoginAsync(credentials.Username, credentials.TemporaryPassword);
        var manager = (await (await _client.PostJsonAsync("/api/v1/auth/change-password",
            new { currentPassword = credentials.TemporaryPassword, newPassword = "QuanLyKhu2026" }, login.AccessToken)).ReadAsync<TokenResponse>()).AccessToken;
        var range = new { fromMonth = $"{f.Previous:yyyy-MM}", toMonth = $"{f.Month:yyyy-MM}", includeDrafts = true };

        using var book = await ExportAsync(manager, range);
        Rows(book.Worksheet("Chi tiết")).Should().HaveCount(4).And.OnlyContain(r => r["Phòng"].GetString().StartsWith('P'));
        (await _client.PostJsonAsync(ExportUrl, new { propertyIds = new[] { f.PropertyB }, range.fromMonth, range.toMonth }, manager, idempotencyKey: null))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task X5_ElectricityUnitPrice_FromPriceSnapshotOnLine()
    {
        var owner = await _client.CreateActiveOwnerAsync();
        var token = owner.Tokens.AccessToken;
        var today = TestData.Today(factory);
        var month = new DateOnly(today.Year, today.Month, 1);
        var previous = month.AddMonths(-1);
        var propertyId = await _client.CreatePropertyAsync(token, anchorDay: 1, chargeMode: "Postpaid");
        var electricity = await _client.FeeIdAsync(token, propertyId, "Điện");
        (await _client.PostJsonAsync($"/api/v1/fee-types/{electricity}/prices", new { effectiveFrom = new DateOnly(2026, 1, 1), unitPrice = 3800 }, token))
            .IsSuccessStatusCode.Should().BeTrue();
        var roomId = await _client.CreateRoomAsync(token, propertyId);
        var meterId = await _client.InstallMeterAsync(token, roomId, electricity, previous.AddDays(-1), 1000);
        var contractId = await _client.CreateContractAsync(token, roomId, await _client.CreateRenterAsync(token), previous);
        (await _client.PostJsonAsync($"/api/v1/contracts/{contractId}/activate",
            new { handoverReadings = new[] { new { meterId, value = (decimal?)null } } }, token)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await _client.PutJsonAsync($"/api/v1/properties/{propertyId}/meter-readings",
                new { readings = new[] { new { meterId, contractId, closingPeriodStart = previous, readingDate = month, value = 1100m } } }, token))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        await GenerateAsync(token, propertyId, previous);
        await FinalizeAsync(token, contractId, previous);

        using var book = await ExportAsync(token, new { fromMonth = $"{previous:yyyy-MM}", toMonth = $"{previous:yyyy-MM}" });

        var row = Rows(book.Worksheet("Chi tiết")).Single();
        row["Đơn giá điện"].GetString().Should().Be("3.800");
        row["Điện nước"].GetValue<decimal>().Should().Be(100 * 3800);
        row["Đơn giá nước"].GetString().Should().BeEmpty();
    }
}
