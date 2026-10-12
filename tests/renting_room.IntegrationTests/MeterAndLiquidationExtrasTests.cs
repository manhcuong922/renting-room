using System.Net;
using System.Text.Json;
using renting_room.IntegrationTests.Infrastructure;

namespace renting_room.IntegrationTests;

/// <summary>
/// 12/10/2026 — M06: lắp công tơ hàng loạt (MT-UC-01), lọc lưới theo khoản thu, cảnh báo ngày ghi lệch xa cuối kỳ (MT-BR-05),
/// sản lượng phòng trống (MT-BR-14); M05: xem trước thanh lý (CT-UC-08).
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class MeterAndLiquidationExtrasTests(ApiFactory factory)
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task BulkInstall_AllOrNothing_SheetFilterByFee_FarReadingDateWarns_VacantUsageReported()
    {
        var owner = await _client.CreateActiveOwnerAsync();
        var token = owner.Tokens.AccessToken;
        var today = TestData.Today(factory);
        var month = new DateOnly(today.Year, today.Month, 1).AddMonths(-1);
        var propertyId = await _client.CreatePropertyAsync(token, anchorDay: 1, chargeMode: "Postpaid");
        var electricity = await _client.FeeIdAsync(token, propertyId, "Điện");
        var water = await _client.FeeIdAsync(token, propertyId, "Nước");
        await _client.PostJsonAsync($"/api/v1/fee-types/{electricity}/prices", new { effectiveFrom = month.AddDays(-30), unitPrice = 3500 }, token);
        await _client.PostJsonAsync($"/api/v1/fee-types/{water}/prices", new { effectiveFrom = month.AddDays(-30), unitPrice = 25000 }, token);
        var roomA = await _client.CreateRoomAsync(token, propertyId, "A1");
        var roomB = await _client.CreateRoomAsync(token, propertyId, "A2");
        var installedDate = month.AddDays(-10);

        // Lắp điện cho cả 2 phòng bằng 1 lệnh.
        Task<HttpResponseMessage> BulkAsync(params object[] rows) =>
            _client.PostJsonAsync($"/api/v1/properties/{propertyId}/meters/bulk", new { feeTypeId = electricity, installedDate, rows }, token);
        var installed = await BulkAsync(new { roomId = roomA, serialNo = "E-A1", initialValue = 100 }, new { roomId = roomB, serialNo = (string?)null, initialValue = 500 });
        installed.StatusCode.Should().Be(HttpStatusCode.OK, await installed.Content.ReadAsStringAsync());
        var result = await installed.ReadAsync<JsonElement>();
        result.GetProperty("installed").GetInt32().Should().Be(2);
        result.GetProperty("meterIds").GetArrayLength().Should().Be(2);

        // Lỗi 1 dòng (phòng đã có công tơ điện) ⇒ không lưu dòng nào, kể cả dòng hợp lệ.
        var roomC = await _client.CreateRoomAsync(token, propertyId, "A3");
        var invalid = await BulkAsync(new { roomId = roomC, serialNo = "E-A3", initialValue = 0 }, new { roomId = roomA, serialNo = "E-X", initialValue = 0 });
        invalid.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        var problem = await invalid.ReadAsync<JsonElement>();
        problem.GetProperty("code").GetString().Should().Be("BULK_INSTALL_INVALID");
        problem.GetProperty("rowErrors").EnumerateArray().Select(e => e.GetProperty("index").GetInt32()).Should().Equal(1);
        (await (await _client.GetAsync($"/api/v1/rooms/{roomC}/meters", token)).ReadAsync<List<JsonElement>>()).Should().BeEmpty();

        // Phòng A1 có thêm công tơ nước; HĐ nhận phòng điện ở 130 (công tơ chạy 30 kWh lúc phòng trống).
        var meters = await (await _client.GetAsync($"/api/v1/rooms/{roomA}/meters", token)).ReadAsync<List<JsonElement>>();
        var electricMeter = meters.Single().GetProperty("id").GetGuid();
        var waterMeter = await _client.InstallMeterAsync(token, roomA, water, installedDate, 10);
        var renter = await _client.CreateRenterAsync(token);
        var contractId = await (await _client.PostJsonAsync("/api/v1/contracts", new
        {
            roomId = roomA,
            contract = new { representativeRenterId = renter, startDate = month, monthlyRent = 3_000_000, depositAmount = 0, occupants = new[] { new { renterId = renter } } }
        }, token)).ReadIdAsync();
        var activated = await _client.PostJsonAsync($"/api/v1/contracts/{contractId}/activate", new
        {
            handoverReadings = new[] { new { meterId = electricMeter, value = (decimal?)130 }, new { meterId = waterMeter, value = (decimal?)null } }
        }, token);
        activated.StatusCode.Should().Be(HttpStatusCode.NoContent, await activated.Content.ReadAsStringAsync());

        // Lưới ghi chỉ số: lọc theo khoản thu.
        var sheetUrl = $"/api/v1/properties/{propertyId}/meter-reading-sheet?billingMonth={month:yyyy-MM}";
        (await (await _client.GetAsync(sheetUrl, token)).ReadAsync<JsonElement>()).GetProperty("rows").GetArrayLength().Should().Be(2);
        var filtered = (await (await _client.GetAsync($"{sheetUrl}&feeTypeId={water}", token)).ReadAsync<JsonElement>()).GetProperty("rows");
        filtered.EnumerateArray().Select(r => r.GetProperty("meterId").GetGuid()).Should().Equal(waterMeter);

        // Ngày ghi quanh cuối kỳ ⇒ không cảnh báo; ghi từ ngày 6 của kỳ ⇒ cảnh báo nhưng vẫn lưu.
        async Task<JsonElement> SaveAsync(Guid meterId, DateOnly readingDate, decimal value)
        {
            var response = await _client.PutJsonAsync($"/api/v1/properties/{propertyId}/meter-readings",
                new { readings = new[] { new { meterId, contractId, closingPeriodStart = month, readingDate, value } } }, token);
            response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
            return await response.ReadAsync<JsonElement>();
        }
        (await SaveAsync(electricMeter, month.AddMonths(1).AddDays(-1), 200)).GetProperty("warnings").GetArrayLength().Should().Be(0);
        var far = await SaveAsync(waterMeter, month.AddDays(5), 12);
        far.GetProperty("saved").GetInt32().Should().Be(1);
        var warning = far.GetProperty("warnings").EnumerateArray().Single();
        warning.GetProperty("index").GetInt32().Should().Be(0);
        warning.GetProperty("code").GetString().Should().Be("READING_DATE_FAR_FROM_PERIOD_END");

        // Sản lượng phòng trống: 100 → 130 của công tơ điện A1; nước nhận phòng đúng số cũ ⇒ không có dòng.
        var vacant = await (await _client.GetAsync($"/api/v1/properties/{propertyId}/meters/vacant-usage", token)).ReadAsync<JsonElement>();
        var row = vacant.GetProperty("rows").EnumerateArray().Single();
        row.GetProperty("roomCode").GetString().Should().Be("A1");
        row.GetProperty("fromValue").GetDecimal().Should().Be(100);
        row.GetProperty("toValue").GetDecimal().Should().Be(130);
        row.GetProperty("quantity").GetDecimal().Should().Be(30);
        var total = vacant.GetProperty("totals").EnumerateArray().Single();
        total.GetProperty("feeTypeId").GetGuid().Should().Be(electricity);
        total.GetProperty("quantity").GetDecimal().Should().Be(30);
        var outOfRange = await (await _client.GetAsync($"/api/v1/properties/{propertyId}/meters/vacant-usage?from={month.AddDays(1):yyyy-MM-dd}", token))
            .ReadAsync<JsonElement>();
        outOfRange.GetProperty("rows").GetArrayLength().Should().Be(0, "ngày nhận phòng nằm trước khoảng lọc");
    }

    [Fact]
    public async Task LiquidationPreview_ListsWhatBlocksCompletion_UntilFinalInvoiceIsFinalized()
    {
        var owner = await _client.CreateActiveOwnerAsync();
        var token = owner.Tokens.AccessToken;
        var today = TestData.Today(factory);
        var start = TestData.StartWithinOnePeriod(today, 10);
        var s = await _client.CreateActiveContractAsync(token, start, anchorDay: start.Day);
        var url = $"/api/v1/contracts/{s.ContractId}/liquidation/preview";
        async Task<JsonElement> PreviewAsync() => await (await _client.GetAsync(url, token)).ReadAsync<JsonElement>();
        static List<string?> Blockers(JsonElement preview) => preview.GetProperty("blockers").EnumerateArray().Select(b => b.GetString()).ToList();

        // Chưa bắt đầu thanh lý.
        var preview = await PreviewAsync();
        preview.GetProperty("status").GetString().Should().Be("Active");
        preview.GetProperty("canComplete").GetBoolean().Should().BeFalse();
        Blockers(preview).Should().NotBeEmpty();

        // Đang thanh lý, chưa lập phiếu quyết toán.
        (await _client.PostJsonAsync($"/api/v1/contracts/{s.ContractId}/liquidation/start", new { actualEndDate = today, reason = "MutualAgreement" }, token))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);
        preview = await PreviewAsync();
        preview.GetProperty("status").GetString().Should().Be("Liquidating");
        preview.GetProperty("finalInvoiceId").ValueKind.Should().Be(JsonValueKind.Null);
        preview.GetProperty("deposit").GetProperty("amount").GetDecimal().Should().Be(3_500_000);
        var withoutFinal = Blockers(preview);
        withoutFinal.Should().ContainSingle();
        var completeNow = await _client.PostJsonAsync($"/api/v1/contracts/{s.ContractId}/liquidation/complete", new { settlement = "CollectAll" }, token);
        (await completeNow.ReadProblemCodeAsync()).Should().Be(withoutFinal[0], "xem trước báo đúng mã mà lệnh hoàn tất sẽ trả");

        // Phiếu quyết toán còn nháp ⇒ vẫn chặn.
        var final = await _client.PostJsonAsync($"/api/v1/contracts/{s.ContractId}/final-invoice", new { finalReadings = (object?)null }, token);
        final.StatusCode.Should().Be(HttpStatusCode.Created, await final.Content.ReadAsStringAsync());
        var invoiceId = (await final.ReadAsync<JsonElement>()).GetProperty("summary").GetProperty("id").GetGuid();
        preview = await PreviewAsync();
        preview.GetProperty("finalInvoiceStatus").GetString().Should().Be("Draft");
        preview.GetProperty("hasOtherDraftInvoices").GetBoolean().Should().BeFalse();
        preview.GetProperty("canComplete").GetBoolean().Should().BeFalse();

        // Đã chốt: còn nợ ⇒ hoàn tất được nhưng phải chọn cách xử lý nợ.
        (await _client.PostJsonAsync($"/api/v1/invoices/{invoiceId}/finalize", null, token)).StatusCode.Should().Be(HttpStatusCode.OK);
        preview = await PreviewAsync();
        preview.GetProperty("finalInvoiceId").GetGuid().Should().Be(invoiceId);
        preview.GetProperty("finalInvoiceStatus").GetString().Should().Be("Finalized");
        Blockers(preview).Should().BeEmpty();
        preview.GetProperty("canComplete").GetBoolean().Should().BeTrue();
        preview.GetProperty("needsDebtDecision").GetBoolean().Should().BeTrue();
        var unpaid = preview.GetProperty("unpaidInvoices").EnumerateArray().Single();
        unpaid.GetProperty("invoiceId").GetGuid().Should().Be(invoiceId);
        unpaid.GetProperty("outstanding").GetDecimal().Should().Be(preview.GetProperty("outstanding").GetDecimal());

        (await (await _client.PostJsonAsync($"/api/v1/contracts/{s.ContractId}/liquidation/complete", new { settlement = (string?)null }, token))
            .ReadProblemCodeAsync()).Should().Be("CONTRACT_HAS_DEBT");
        (await _client.PostJsonAsync($"/api/v1/contracts/{s.ContractId}/liquidation/complete", new { settlement = "CollectAll" }, token))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);
        preview = await PreviewAsync();
        preview.GetProperty("status").GetString().Should().Be("Ended");
        preview.GetProperty("canComplete").GetBoolean().Should().BeFalse();
        (await _client.GetAsync($"/api/v1/contracts/{Guid.NewGuid()}/liquidation/preview", token)).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
