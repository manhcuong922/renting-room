using System.Net;
using System.Text.Json;
using renting_room.IntegrationTests.Infrastructure;

namespace renting_room.IntegrationTests;

/// <summary>
/// M07 BL-UC-18 (BL-BR-42..46): chủ trọ ngừng dùng hệ thống vài kỳ (thu tay ngoài) rồi dùng lại — lập phiếu kỳ mới với
/// <c>skipUnbilledPeriods</c> ghi một khoảng ngắt tính tiền thay vì bắt lập các kỳ bỏ trống.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class BillingGapTests(ApiFactory factory)
{
    private const decimal Rent = 3_000_000;
    private readonly HttpClient _client = factory.CreateClient();

    /// <summary>Kỳ 0..4 là 5 tháng tròn liên tiếp đã qua (kỳ 0 = 5 tháng trước), công tơ điện 3.500đ/kWh, số nhận phòng 100.</summary>
    private sealed record Setup(string Token, Guid PropertyId, Guid ContractId, Guid MeterId, DateOnly[] Months);

    private async Task<Setup> ArrangeAsync(string chargeMode = "Postpaid", DateOnly? start = null)
    {
        var owner = await _client.CreateActiveOwnerAsync();
        var token = owner.Tokens.AccessToken;
        var today = TestData.Today(factory);
        var first = start ?? new DateOnly(today.Year, today.Month, 1).AddMonths(-5);
        var propertyId = await _client.CreatePropertyAsync(token, anchorDay: 1, chargeMode: chargeMode);
        var roomId = await _client.CreateRoomAsync(token, propertyId);
        var electricity = await _client.FeeIdAsync(token, propertyId, "Điện");
        await _client.PostJsonAsync($"/api/v1/fee-types/{electricity}/prices", new { effectiveFrom = first.AddDays(-30), unitPrice = 3500 }, token);
        var meter = await _client.InstallMeterAsync(token, roomId, electricity, first.AddDays(-1), 100);
        var renter = await _client.CreateRenterAsync(token);
        var created = await _client.PostJsonAsync("/api/v1/contracts", new
        {
            roomId,
            contract = new
            {
                representativeRenterId = renter, startDate = first, monthlyRent = Rent, depositAmount = 0,
                occupants = new[] { new { renterId = renter } }
            }
        }, token);
        created.StatusCode.Should().Be(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        var contractId = await created.ReadIdAsync();
        (await _client.PostJsonAsync($"/api/v1/contracts/{contractId}/activate",
            new { handoverReadings = new[] { new { meterId = meter, value = (decimal?)null } } }, token)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        return new Setup(token, propertyId, contractId, meter, Enumerable.Range(0, 5).Select(first.AddMonths).ToArray());
    }

    /// <summary>Chỉ số cuối khi bắt đầu thanh lý (H-02): công tơ chưa chạy thêm kể từ chỉ số gần nhất tới ngày trả phòng.</summary>
    private async Task<object> FinalAtAsync(Setup s, DateOnly date)
    {
        var readings = await (await _client.GetAsync($"/api/v1/meters/{s.MeterId}/readings", s.Token)).ReadAsync<List<JsonElement>>();
        var latest = readings.First(r => DateOnly.Parse(r.GetProperty("readingDate").GetString()!) <= date).GetProperty("value").GetDecimal();
        return new[] { new { meterId = s.MeterId, value = (decimal?)latest } };
    }

    private Task<HttpResponseMessage> SaveReadingAsync(Setup s, DateOnly month, decimal value) =>
        _client.PutJsonAsync($"/api/v1/properties/{s.PropertyId}/meter-readings", new
        {
            readings = new[]
            {
                new { meterId = s.MeterId, contractId = s.ContractId, closingPeriodStart = month, readingDate = month.AddMonths(1).AddDays(-1), value }
            }
        }, s.Token);

    private async Task<JsonElement> GenerateAsync(Setup s, DateOnly month, bool? skip = null) =>
        await (await _client.PostJsonAsync("/api/v1/invoices/generate",
            new { propertyId = s.PropertyId, billingMonth = $"{month:yyyy-MM}", skipUnbilledPeriods = skip }, s.Token)).ReadAsync<JsonElement>();

    private async Task<JsonElement> InvoiceForAsync(Setup s, DateOnly month)
    {
        var page = await (await _client.GetAsync($"/api/v1/invoices?contractId={s.ContractId}&billingMonth={month:yyyy-MM}", s.Token)).ReadAsync<JsonElement>();
        var id = page.GetProperty("items")[0].GetProperty("id").GetGuid();
        return await (await _client.GetAsync($"/api/v1/invoices/{id}", s.Token)).ReadAsync<JsonElement>();
    }

    private static Guid Id(JsonElement invoice) => invoice.GetProperty("summary").GetProperty("id").GetGuid();

    /// <summary>Lập + (tùy chọn) chốt phiếu kỳ đầu (tháng 0): điện 100 → 200.</summary>
    private async Task BillFirstMonthAsync(Setup s, bool finalize)
    {
        (await SaveReadingAsync(s, s.Months[0], 200)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await GenerateAsync(s, s.Months[0])).GetProperty("created").GetInt32().Should().Be(1);
        if (finalize)
            (await _client.PostJsonAsync($"/api/v1/invoices/{Id(await InvoiceForAsync(s, s.Months[0]))}/finalize", null, s.Token))
                .StatusCode.Should().Be(HttpStatusCode.OK);
    }

    private async Task<JsonElement> GapsAsync(Setup s) =>
        await (await _client.GetAsync($"/api/v1/contracts/{s.ContractId}/billing-gaps", s.Token)).ReadAsync<JsonElement>();

    private static string? Reason(JsonElement generated) => generated.GetProperty("skipped").EnumerateArray().Select(x => x.GetProperty("reason").GetString()).SingleOrDefault();

    // BL-BR-42: không bật cờ ⇒ vẫn chặn, nhưng kèm danh sách kỳ chưa lập để giao diện hỏi lại.
    [Fact]
    public async Task WithoutFlag_StillBlocked_ListsUnbilledPeriods()
    {
        var s = await ArrangeAsync();
        await BillFirstMonthAsync(s, finalize: true);
        await SaveReadingAsync(s, s.Months[3], 480);
        await SaveReadingAsync(s, s.Months[4], 530);

        var generated = await GenerateAsync(s, s.Months[4]);

        generated.GetProperty("created").GetInt32().Should().Be(0);
        Reason(generated).Should().Be("PREVIOUS_PERIOD_NOT_BILLED");
        var skipped = generated.GetProperty("skipped")[0];
        skipped.GetProperty("unbilledPeriods").EnumerateArray().Select(p => DateOnly.Parse(p.GetProperty("start").GetString()!))
            .Should().Equal(s.Months[1], s.Months[2], s.Months[3]);
        (await GapsAsync(s)).GetArrayLength().Should().Be(0);
    }

    // BL-BR-42, 44: bật cờ ⇒ 1 khoảng ngắt 3 kỳ; điện = 530 − 480 = 50 kWh (không nối ngược về 200); tổng 3.175.000.
    [Fact]
    public async Task WithFlag_CreatesGap_StartReadingIsClosingReadingOfLastGapPeriod()
    {
        var s = await ArrangeAsync();
        await BillFirstMonthAsync(s, finalize: true);
        await SaveReadingAsync(s, s.Months[3], 480);
        await SaveReadingAsync(s, s.Months[4], 530);

        var generated = await GenerateAsync(s, s.Months[4], skip: true);

        generated.GetProperty("created").GetInt32().Should().Be(1);
        generated.GetProperty("gapsCreated").GetInt32().Should().Be(1);
        var invoice = await InvoiceForAsync(s, s.Months[4]);
        invoice.GetProperty("lines").EnumerateArray().Select(l => (l.GetProperty("type").GetString(), l.GetProperty("amount").GetDecimal()))
            .Should().Equal(("Rent", Rent), ("Metered", 175_000m));
        invoice.GetProperty("summary").GetProperty("totalAmount").GetDecimal().Should().Be(3_175_000);

        var gaps = await GapsAsync(s);
        gaps.GetArrayLength().Should().Be(1);
        DateOnly.Parse(gaps[0].GetProperty("fromDate").GetString()!).Should().Be(s.Months[1]);
        DateOnly.Parse(gaps[0].GetProperty("toDate").GetString()!).Should().Be(s.Months[4].AddDays(-1));
        gaps[0].GetProperty("periodCount").GetInt32().Should().Be(3);
        gaps[0].GetProperty("resumedInvoiceId").GetGuid().Should().Be(Id(invoice));

        var periods = await (await _client.GetAsync($"/api/v1/contracts/{s.ContractId}/billing-periods?until={s.Months[4]:yyyy-MM-dd}", s.Token)).ReadAsync<JsonElement>();
        periods.EnumerateArray().Where(p => p.GetProperty("skipped").GetBoolean()).Select(p => DateOnly.Parse(p.GetProperty("start").GetString()!))
            .Should().Equal(s.Months[1], s.Months[2], s.Months[3]);
    }

    // BL-BR-44: thiếu chỉ số chốt kỳ cuối khoảng ⇒ MISSING_READING, không được tính 200 → 530 (330 kWh); không ghi khoảng ngắt.
    [Fact]
    public async Task WithFlag_MissingClosingReadingOfLastGapPeriod_CreatesNothing()
    {
        var s = await ArrangeAsync();
        await BillFirstMonthAsync(s, finalize: true);
        await SaveReadingAsync(s, s.Months[4], 530);

        var generated = await GenerateAsync(s, s.Months[4], skip: true);

        generated.GetProperty("created").GetInt32().Should().Be(0);
        generated.GetProperty("gapsCreated").GetInt32().Should().Be(0);
        Reason(generated).Should().Be("MISSING_READING");
        (await GapsAsync(s)).GetArrayLength().Should().Be(0);
    }

    // BL-BR-43: phiếu liền trước khoảng còn nháp ⇒ chặn (hướng 1: chốt hoặc xóa nháp trước).
    [Fact]
    public async Task WithFlag_PreviousInvoiceStillDraft_Blocked_ThenDeletingDraftBeforeFirstPeriodExtendsGap()
    {
        var s = await ArrangeAsync();
        await BillFirstMonthAsync(s, finalize: false);
        await SaveReadingAsync(s, s.Months[3], 480);
        await SaveReadingAsync(s, s.Months[4], 530);

        Reason(await GenerateAsync(s, s.Months[4], skip: true)).Should().Be("GAP_PREVIOUS_NOT_FINALIZED");
        (await GapsAsync(s)).GetArrayLength().Should().Be(0);

        // Xóa nháp kỳ 0 ⇒ khoảng ngắt phủ luôn kỳ 0 (4 kỳ), bắt đầu từ "Tính tiền từ ngày".
        var draftId = Id(await InvoiceForAsync(s, s.Months[0]));
        (await _client.DeleteAsync($"/api/v1/invoices/{draftId}", s.Token)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await GenerateAsync(s, s.Months[4], skip: true)).GetProperty("created").GetInt32().Should().Be(1);
        var gaps = await GapsAsync(s);
        DateOnly.Parse(gaps[0].GetProperty("fromDate").GetString()!).Should().Be(s.Months[0]);
        gaps[0].GetProperty("periodCount").GetInt32().Should().Be(4);
    }

    // BL-BR-45: xóa nháp phiếu sau khoảng ⇒ khoảng ngắt mất theo; lập lại không cờ lại bị chặn.
    [Fact]
    public async Task DeletingFirstDraftAfterGap_RemovesGap()
    {
        var s = await ArrangeAsync();
        await BillFirstMonthAsync(s, finalize: true);
        await SaveReadingAsync(s, s.Months[3], 480);
        await SaveReadingAsync(s, s.Months[4], 530);
        await GenerateAsync(s, s.Months[4], skip: true);

        var draftId = Id(await InvoiceForAsync(s, s.Months[4]));
        (await _client.DeleteAsync($"/api/v1/invoices/{draftId}", s.Token)).StatusCode.Should().Be(HttpStatusCode.NoContent);

        (await GapsAsync(s)).GetArrayLength().Should().Be(0);
        Reason(await GenerateAsync(s, s.Months[4])).Should().Be("PREVIOUS_PERIOD_NOT_BILLED");
    }

    // BL-UC-18: generate-bulk cùng cờ; chạy lại không tạo trùng khoảng ngắt.
    [Fact]
    public async Task Bulk_WithFlag_CreatesGap_AndIsIdempotent()
    {
        var s = await ArrangeAsync();
        await BillFirstMonthAsync(s, finalize: true);
        await SaveReadingAsync(s, s.Months[3], 480);
        await SaveReadingAsync(s, s.Months[4], 530);

        async Task<JsonElement> BulkAsync(bool? skip) => (await (await _client.PostJsonAsync("/api/v1/invoices/generate-bulk",
            new { propertyIds = new[] { s.PropertyId }, billingMonth = $"{s.Months[4]:yyyy-MM}", skipUnbilledPeriods = skip }, s.Token))
            .ReadAsync<JsonElement>()).EnumerateArray().Single().GetProperty("result");

        var without = await BulkAsync(null);
        without.GetProperty("created").GetInt32().Should().Be(0);
        without.GetProperty("skipped")[0].GetProperty("unbilledPeriods").GetArrayLength().Should().Be(3);

        var first = await BulkAsync(true);
        first.GetProperty("created").GetInt32().Should().Be(1);
        first.GetProperty("gapsCreated").GetInt32().Should().Be(1);

        var again = await BulkAsync(true);
        again.GetProperty("created").GetInt32().Should().Be(0);
        again.GetProperty("gapsCreated").GetInt32().Should().Be(0);
        again.GetProperty("skipped")[0].GetProperty("reason").GetString().Should().Be("EXISTS");
        (await GapsAsync(s)).GetArrayLength().Should().Be(1);
    }

    // BL-BR-46 (3): kỳ trong khoảng ngắt đã khóa giá thuê (CT-BR-05).
    [Fact]
    public async Task RentChange_InsideGap_Locked_AfterGap_Allowed()
    {
        var s = await ArrangeAsync();
        await BillFirstMonthAsync(s, finalize: true);
        await SaveReadingAsync(s, s.Months[3], 480);
        await SaveReadingAsync(s, s.Months[4], 530);
        await GenerateAsync(s, s.Months[4], skip: true);

        var inside = await _client.PostJsonAsync($"/api/v1/contracts/{s.ContractId}/rent-terms",
            new { effectiveFrom = s.Months[2], monthlyRent = 3_500_000 }, s.Token);
        (await inside.ReadProblemCodeAsync()).Should().Be("PERIOD_ALREADY_BILLED");

        (await _client.PostJsonAsync($"/api/v1/contracts/{s.ContractId}/rent-terms",
            new { effectiveFrom = s.Months[4], monthlyRent = 3_500_000 }, s.Token)).StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    // BL-BR-43 (1): khu thu trước không hỗ trợ khoảng ngắt.
    [Fact]
    public async Task WithFlag_PrepaidProperty_Rejected()
    {
        var s = await ArrangeAsync("Prepaid");
        (await GenerateAsync(s, s.Months[0])).GetProperty("created").GetInt32().Should().Be(1);
        (await _client.PostJsonAsync($"/api/v1/invoices/{Id(await InvoiceForAsync(s, s.Months[0]))}/finalize", null, s.Token))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        Reason(await GenerateAsync(s, s.Months[4], skip: true)).Should().Be("GAP_NOT_SUPPORTED_PREPAID");
    }

    // BL-BR-43 (3): quá 12 kỳ một lần ⇒ chặn (chống gõ nhầm tháng). "Tính tiền từ ngày" cách đây đúng 1 năm, lập tháng sau ⇒ 13 kỳ chưa lập.
    [Fact]
    public async Task WithFlag_MoreThanTwelveUnbilledPeriods_Rejected()
    {
        var s = await ArrangeAsync(start: TestData.Today(factory).AddYears(-1));
        var today = TestData.Today(factory);
        var nextMonth = new DateOnly(today.Year, today.Month, 1).AddMonths(1);

        var generated = await GenerateAsync(s, nextMonth, skip: true);

        Reason(generated).Should().Be("GAP_TOO_LONG");
        generated.GetProperty("gapsCreated").GetInt32().Should().Be(0);
        (await GapsAsync(s)).GetArrayLength().Should().Be(0);
    }

    // BL-BR-43 (4): khu đổi ngày chốt trong các kỳ bị bỏ ⇒ chặn (kỳ chuyển tiếp làm kỳ bị bỏ không xác định được).
    [Fact]
    public async Task WithFlag_ScheduleChangedInsideSkippedPeriods_Rejected()
    {
        var s = await ArrangeAsync();
        await BillFirstMonthAsync(s, finalize: true);
        var changed = await _client.PutJsonAsync($"/api/v1/properties/{s.PropertyId}/billing",
            new { anchorDay = 5, chargeMode = "Postpaid", paymentDueDays = 5, prorationMode = "Daily", noticeDays = 30 }, s.Token);
        changed.StatusCode.Should().Be(HttpStatusCode.OK, await changed.Content.ReadAsStringAsync());

        var generated = await GenerateAsync(s, s.Months[4], skip: true);

        Reason(generated).Should().Be("GAP_SPANS_SCHEDULE_CHANGE");
        (await GapsAsync(s)).GetArrayLength().Should().Be(0);
    }

    // BL-BR-46 (1): hóa đơn trả phòng kỳ cuối sau khoảng nghỉ — cùng cờ, điện = số trả phòng − số chốt kỳ cuối khoảng.
    [Fact]
    public async Task FinalInvoice_WithFlag_CreatesGap_AndStartsFromLastGapReading()
    {
        var s = await ArrangeAsync();
        await BillFirstMonthAsync(s, finalize: true);
        await SaveReadingAsync(s, s.Months[2], 400);
        var endDate = s.Months[3].AddDays(14);
        (await _client.PostJsonAsync($"/api/v1/contracts/{s.ContractId}/liquidation/start",
            new
            {
                actualEndDate = endDate, reason = "Abandoned", note = "Phát hiện bỏ đi ngày 15, để lại ít đồ",
                finalReadings = new[] { new { meterId = s.MeterId, value = (decimal?)450 } }
            }, s.Token)).StatusCode.Should().Be(HttpStatusCode.NoContent);

        var finalReadings = new[] { new { meterId = s.MeterId, value = (decimal?)450 } };
        var blocked = await _client.PostJsonAsync($"/api/v1/contracts/{s.ContractId}/final-invoice", new { finalReadings }, s.Token);
        (await blocked.ReadProblemCodeAsync()).Should().Be("PREVIOUS_PERIOD_NOT_BILLED");

        var created = await _client.PostJsonAsync($"/api/v1/contracts/{s.ContractId}/final-invoice", new { finalReadings, skipUnbilledPeriods = true }, s.Token);
        created.StatusCode.Should().Be(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        var final = await created.ReadAsync<JsonElement>();
        final.GetProperty("lines").EnumerateArray().Single(l => l.GetProperty("type").GetString() == "Metered")
            .GetProperty("quantity").GetDecimal().Should().Be(50, "450 − 400 (chốt kỳ cuối khoảng), không nối ngược về 200");
        var gaps = await GapsAsync(s);
        gaps.GetArrayLength().Should().Be(1);
        gaps[0].GetProperty("periodCount").GetInt32().Should().Be(2);
    }

    // BL-BR-46 (2): thanh lý "đã xử lý ngoài hệ thống" — không hóa đơn trả phòng, khoảng ngắt đóng bởi thanh lý.
    [Fact]
    public async Task Liquidation_ExternalSettlement_NoFinalInvoice_GapClosedByLiquidation()
    {
        var s = await ArrangeAsync();
        await BillFirstMonthAsync(s, finalize: true);
        var endDate = s.Months[3].AddDays(14);
        (await _client.PostJsonAsync($"/api/v1/contracts/{s.ContractId}/liquidation/start",
            new { actualEndDate = endDate, reason = "Abandoned", note = "Phát hiện bỏ đi ngày 15, để lại ít đồ", finalReadings = await FinalAtAsync(s, endDate) },
            s.Token)).StatusCode.Should().Be(HttpStatusCode.NoContent);

        // Không cờ ⇒ như cũ: còn phải lập hóa đơn trả phòng.
        (await _client.PostJsonAsync($"/api/v1/contracts/{s.ContractId}/liquidation/complete", new { settlement = "CollectAll" }, s.Token))
            .StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        // Thiếu ghi chú ⇒ chặn.
        (await (await _client.PostJsonAsync($"/api/v1/contracts/{s.ContractId}/liquidation/complete",
            new { settlement = "CollectAll", externalSettlement = new { note = "" } }, s.Token)).ReadProblemCodeAsync()).Should().Be("EXTERNAL_NOTE_REQUIRED");

        var completed = await _client.PostJsonAsync($"/api/v1/contracts/{s.ContractId}/liquidation/complete",
            new { settlement = "CollectAll", externalSettlement = new { note = "Đã thu tay đến ngày đi" } }, s.Token);
        completed.StatusCode.Should().Be(HttpStatusCode.NoContent, await completed.Content.ReadAsStringAsync());

        var contract = await (await _client.GetAsync($"/api/v1/contracts/{s.ContractId}", s.Token)).ReadAsync<JsonElement>();
        contract.GetProperty("status").GetString().Should().Be("Ended");
        var invoices = await (await _client.GetAsync($"/api/v1/invoices?contractId={s.ContractId}", s.Token)).ReadAsync<JsonElement>();
        invoices.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("type").GetString()).Should().OnlyContain(t => t == "Regular");
        var gaps = await GapsAsync(s);
        gaps.GetArrayLength().Should().Be(1);
        DateOnly.Parse(gaps[0].GetProperty("fromDate").GetString()!).Should().Be(s.Months[1]);
        DateOnly.Parse(gaps[0].GetProperty("toDate").GetString()!).Should().Be(endDate);
        gaps[0].GetProperty("periodCount").GetInt32().Should().Be(3);
        gaps[0].GetProperty("resumedInvoiceId").ValueKind.Should().Be(JsonValueKind.Null);
    }

    // BL-BR-46 (2): kỳ cuối đã có phiếu tháng ⇒ không dùng được "đã xử lý ngoài".
    [Fact]
    public async Task Liquidation_ExternalSettlement_RejectedWhenFinalPeriodAlreadyInvoiced()
    {
        var s = await ArrangeAsync();
        await BillFirstMonthAsync(s, finalize: true);
        (await _client.PostJsonAsync($"/api/v1/contracts/{s.ContractId}/liquidation/start",
            new { actualEndDate = s.Months[1].AddDays(-1), reason = "MutualAgreement", finalReadings = await FinalAtAsync(s, s.Months[1].AddDays(-1)) },
            s.Token)).StatusCode.Should().Be(HttpStatusCode.NoContent);

        var response = await _client.PostJsonAsync($"/api/v1/contracts/{s.ContractId}/liquidation/complete",
            new { settlement = "CollectAll", externalSettlement = new { note = "Đã thu tay" } }, s.Token);

        (await response.ReadProblemCodeAsync()).Should().Be("EXTERNAL_SETTLEMENT_NOT_ALLOWED");
    }
}
