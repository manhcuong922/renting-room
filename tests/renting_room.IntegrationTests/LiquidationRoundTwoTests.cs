using System.Net;
using System.Text.Json;
using renting_room.IntegrationTests.Infrastructure;

namespace renting_room.IntegrationTests;

/// <summary>
/// 20/10/2026 (Lượt 2): màn thanh lý gom tất cả + hoàn tất một lần (M05 CT-UC-08, CT-BR-11/12), kỳ cuối theo ngày / trọn kỳ, báo trả phòng bỏ qua
/// hóa đơn tháng (CT-UC-07), cọc "Không hoàn" (PM-BR-33), khoản sau khi hóa đơn trả phòng chốt (BL-BR-32), điều chỉnh số cuối sau thanh lý
/// (M06 MT-BR-19) và người mới nhận phòng nhập số nhỏ hơn (CT-BR-02). Khu chốt ngày 1, tiền phòng 3.000.000đ, internet 150.000đ, điện 3.500đ/kWh.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class LiquidationRoundTwoTests(ApiFactory factory)
{
    private const decimal Rent = 3_000_000;
    private const decimal Internet = 150_000;
    private const decimal Price = 3_500;
    private readonly HttpClient _client = factory.CreateClient();

    private sealed record Setup(string Token, Guid PropertyId, Guid RoomId, Guid ContractId, Guid MeterId, DateOnly Start, DateOnly Today, Guid RenterId);

    /// <summary>HĐ từ ngày 1 của 2 tháng trước, công tơ điện nhận phòng ở 100, có internet theo phòng.</summary>
    private async Task<Setup> ArrangeAsync(string chargeMode = "Postpaid", decimal deposit = 0, string prorationMode = "Daily")
    {
        var token = (await _client.CreateActiveOwnerAsync()).Tokens.AccessToken;
        var today = TestData.Today(factory);
        var start = new DateOnly(today.Year, today.Month, 1).AddMonths(-2);
        var propertyId = await _client.CreatePropertyAsync(token, anchorDay: 1, chargeMode: chargeMode, prorationMode: prorationMode);
        var roomId = await _client.CreateRoomAsync(token, propertyId);
        var electricity = await _client.FeeIdAsync(token, propertyId, "Điện");
        await _client.PostJsonAsync($"/api/v1/fee-types/{electricity}/prices", new { effectiveFrom = start.AddDays(-30), unitPrice = Price }, token);
        var internet = await (await _client.PostJsonAsync($"/api/v1/properties/{propertyId}/fee-types", new
        {
            name = "Internet", group = "Service", chargeBasis = "PerRoom", unit = "phòng", autoAttach = false,
            initialPrice = new { effectiveFrom = start.AddDays(-30), unitPrice = Internet }
        }, token)).ReadIdAsync();
        var meterId = await _client.InstallMeterAsync(token, roomId, electricity, start.AddDays(-1), 100);
        var renter = await _client.CreateRenterAsync(token);
        var created = await _client.PostJsonAsync("/api/v1/contracts", new
        {
            roomId,
            contract = new
            {
                representativeRenterId = renter, startDate = start, monthlyRent = Rent, depositAmount = deposit,
                occupants = new[] { new { renterId = renter } }, fees = new[] { new { feeTypeId = internet } }
            }
        }, token);
        created.StatusCode.Should().Be(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        var contractId = await created.ReadIdAsync();
        (await _client.PostJsonAsync($"/api/v1/contracts/{contractId}/activate",
            new { handoverReadings = new[] { new { meterId, value = (decimal?)null } } }, token)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        return new Setup(token, propertyId, roomId, contractId, meterId, start, today, renter);
    }

    /// <param name="onAnchorDay">Đọc số sáng ngày chốt kỳ sau (thực tế hay gặp) thay vì ngày cuối kỳ.</param>
    private Task<HttpResponseMessage> SaveReadingAsync(Setup s, DateOnly closing, decimal value, bool onAnchorDay = false) =>
        _client.PutJsonAsync($"/api/v1/properties/{s.PropertyId}/meter-readings", new
        {
            readings = new[]
            {
                new { meterId = s.MeterId, contractId = s.ContractId, closingPeriodStart = closing,
                    readingDate = onAnchorDay ? closing.AddMonths(1) : closing.AddMonths(1).AddDays(-1), value }
            }
        }, s.Token);

    private async Task<JsonElement> GenerateAsync(Setup s, DateOnly month) =>
        await (await _client.PostJsonAsync("/api/v1/invoices/generate", new { propertyId = s.PropertyId, billingMonth = $"{month:yyyy-MM}" }, s.Token))
            .ReadAsync<JsonElement>();

    private async Task<JsonElement> InvoiceForAsync(Setup s, DateOnly month)
    {
        var page = await (await _client.GetAsync($"/api/v1/invoices?contractId={s.ContractId}&billingMonth={month:yyyy-MM}", s.Token)).ReadAsync<JsonElement>();
        return await InvoiceAsync(s, page.GetProperty("items")[0].GetProperty("id").GetGuid());
    }

    private async Task<JsonElement> InvoiceAsync(Setup s, Guid id) => await (await _client.GetAsync($"/api/v1/invoices/{id}", s.Token)).ReadAsync<JsonElement>();

    private static Guid Id(JsonElement invoice) => invoice.GetProperty("summary").GetProperty("id").GetGuid();
    private static decimal Total(JsonElement invoice) => invoice.GetProperty("summary").GetProperty("totalAmount").GetDecimal();
    private static decimal Amount(JsonElement invoice, string type) =>
        invoice.GetProperty("lines").EnumerateArray().Single(l => l.GetProperty("type").GetString() == type).GetProperty("amount").GetDecimal();

    private async Task<JsonElement> FinalizeAsync(Setup s, Guid invoiceId)
    {
        var finalized = await _client.PostJsonAsync($"/api/v1/invoices/{invoiceId}/finalize", null, s.Token);
        finalized.StatusCode.Should().Be(HttpStatusCode.OK, await finalized.Content.ReadAsStringAsync());
        return await finalized.ReadAsync<JsonElement>();
    }

    /// <summary>Tháng đầu (kỳ HĐ đầu tiên) lập + chốt với chỉ số cuối kỳ <paramref name="reading"/>.</summary>
    private async Task BillFirstMonthAsync(Setup s, decimal reading)
    {
        (await SaveReadingAsync(s, s.Start, reading)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await GenerateAsync(s, s.Start)).GetProperty("created").GetInt32().Should().Be(1);
        await FinalizeAsync(s, Id(await InvoiceForAsync(s, s.Start)));
    }

    private Task<HttpResponseMessage> StartAsync(Setup s, DateOnly actualEndDate, string? finalPeriodCharge = null) =>
        _client.PostJsonAsync($"/api/v1/contracts/{s.ContractId}/liquidation/start",
            new { actualEndDate, reason = "MutualAgreement", finalPeriodCharge }, s.Token);

    private Task<HttpResponseMessage> ChargeAsync(Setup s, string? finalPeriodCharge) =>
        _client.PutJsonAsync($"/api/v1/contracts/{s.ContractId}/liquidation/final-period-charge", new { finalPeriodCharge }, s.Token);

    private static decimal Money(decimal value) => decimal.Round(value, 0, MidpointRounding.AwayFromZero);

    private async Task<JsonElement> FinalInvoiceAsync(Setup s, decimal reading)
    {
        var created = await _client.PostJsonAsync($"/api/v1/contracts/{s.ContractId}/final-invoice",
            new { finalReadings = new[] { new { meterId = s.MeterId, value = (decimal?)reading } } }, s.Token);
        created.StatusCode.Should().Be(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        return await created.ReadAsync<JsonElement>();
    }

    private async Task<JsonElement> PreviewAsync(Setup s, DateOnly? actualEndDate = null) =>
        await (await _client.GetAsync($"/api/v1/contracts/{s.ContractId}/liquidation/preview" +
            (actualEndDate is { } d ? $"?actualEndDate={d:yyyy-MM-dd}" : ""), s.Token)).ReadAsync<JsonElement>();

    private async Task<List<JsonElement>> ReadingsAsync(Setup s) =>
        await (await _client.GetAsync($"/api/v1/meters/{s.MeterId}/readings", s.Token)).ReadAsync<List<JsonElement>>();

    [Fact]
    public async Task FinalPeriod_DefaultFullPeriod_DailyAndNoneAreOptions_ManualEditNeedsReason_SummaryShown()
    {
        var s = await ArrangeAsync();
        await BillFirstMonthAsync(s, 150);
        var month = s.Start.AddMonths(1);
        var days = DateTime.DaysInMonth(month.Year, month.Month);
        (await StartAsync(s, month.AddDays(14))).StatusCode.Should().Be(HttpStatusCode.NoContent);

        var preview = await PreviewAsync(s);
        preview.GetProperty("finalPeriodCharge").GetString().Should().Be("FullPeriod", "mặc định thu trọn kỳ, không theo cài đặt khu (Daily)");
        preview.GetProperty("finalInvoiceRequired").GetBoolean().Should().BeTrue();
        preview.GetProperty("finalPeriodOptions").EnumerateArray()
            .Select(o => (o.GetProperty("charge").GetString(), o.GetProperty("rent").GetDecimal(), o.GetProperty("services").GetDecimal()))
            .Should().Equal(("FullPeriod", Rent, Internet), ("Daily", Money(Rent * 15 / days), Money(Internet * 15 / days)), ("None", 0m, 0m));

        var final = await FinalInvoiceAsync(s, 170);
        Amount(final, "Rent").Should().Be(Rent);
        Amount(final, "Service").Should().Be(Internet);
        Amount(final, "Metered").Should().Be(20 * Price, "điện luôn theo số công tơ");

        (await ChargeAsync(s, "Daily")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        var daily = await InvoiceAsync(s, Id(final));
        Amount(daily, "Rent").Should().Be(Money(Rent * 15 / days));
        Amount(daily, "Service").Should().Be(Money(Internet * 15 / days));
        (await ChargeAsync(s, "None")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        var none = await InvoiceAsync(s, Id(final));
        Amount(none, "Rent").Should().Be(0);
        Amount(none, "Service").Should().Be(0);
        Amount(none, "Metered").Should().Be(20 * Price, "không thu tiền phòng vẫn tính điện");
        var summary = none.GetProperty("finalPeriod");
        summary.GetProperty("stayedUntil").GetString().Should().Be($"{month.AddDays(14):yyyy-MM-dd}");
        summary.GetProperty("days").GetInt32().Should().Be(15);
        summary.GetProperty("charge").GetString().Should().Be("None");
        summary.GetProperty("rentBilledInvoiceNo").ValueKind.Should().Be(JsonValueKind.Null);

        var metered = none.GetProperty("lines").EnumerateArray().Single(l => l.GetProperty("type").GetString() == "Metered").GetProperty("id").GetGuid();
        (await (await _client.PutJsonAsync($"/api/v1/invoices/{Id(final)}/lines/{metered}", new { amount = 50_000 }, s.Token))
            .ReadProblemCodeAsync()).Should().Be("NOTE_REQUIRED", "hóa đơn trả phòng: mọi dòng sửa tay phải có lý do");
        var edited = await (await _client.PutJsonAsync($"/api/v1/invoices/{Id(final)}/lines/{metered}",
            new { amount = 50_000, note = "Trừ điện thợ sửa máy lạnh" }, s.Token)).ReadAsync<JsonElement>();
        var line = edited.GetProperty("lines").EnumerateArray().Single(l => l.GetProperty("id").GetGuid() == metered);
        line.GetProperty("isManuallyEdited").GetBoolean().Should().BeTrue();
        line.GetProperty("systemAmount").GetDecimal().Should().Be(20 * Price);

        await FinalizeAsync(s, Id(final));
        (await (await ChargeAsync(s, "Daily")).ReadProblemCodeAsync()).Should().Be("FINAL_INVOICE_FINALIZED");

        (await _client.PostJsonAsync($"/api/v1/invoices/{Id(final)}/void", new { reason = "Lập lại" }, s.Token)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await ChargeAsync(s, "Daily")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await InvoiceAsync(s, Id(final))).GetProperty("finalPeriod").GetProperty("charge").GetString()
            .Should().Be("None", "hóa đơn đã hủy giữ đúng cách đã tính (suy từ dòng tiền phòng), không theo lựa chọn mới của HĐ");
    }

    [Fact]
    public async Task PhantomPeriod_TreatedAsMovedOutAtLastBilledEnd_DraftDeleted_NoMoveOutInvoiceNeeded()
    {
        var s = await ArrangeAsync();
        var february = s.Start.AddMonths(1);
        (await SaveReadingAsync(s, s.Start, 150, onAnchorDay: true)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await GenerateAsync(s, s.Start)).GetProperty("created").GetInt32().Should().Be(1);
        await FinalizeAsync(s, Id(await InvoiceForAsync(s, s.Start)));
        (await SaveReadingAsync(s, february, 180, onAnchorDay: true)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await GenerateAsync(s, february)).GetProperty("created").GetInt32().Should().Be(1);
        var phantom = Id(await InvoiceForAsync(s, february));
        (await _client.PostJsonAsync($"/api/v1/invoices/{phantom}/manual-lines",
            new { type = "Surcharge", description = "Phụ thu nước tháng 2", amount = 50_000, note = "Lập trước khi biết người thuê đi" }, s.Token))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        var preview = await PreviewAsync(s, s.Today);
        (await PreviewAsync(s, february.AddDays(-1))).GetProperty("invoicesFromEndDate").EnumerateArray().Single().GetProperty("mustCancel")
            .GetBoolean().Should().BeFalse("nháp kỳ sau ngày trả phòng tự xóa khi bắt đầu thanh lý");
        preview.GetProperty("unbilledPeriods").EnumerateArray().Should().ContainSingle("chỉ có nháp, chưa chốt ⇒ vẫn là kỳ chưa có hóa đơn")
            .Which.GetProperty("start").GetString().Should().Be($"{february:yyyy-MM-dd}");
        var lastBilledEnd = february.AddDays(-1);
        preview.GetProperty("lastBilledEnd").GetString().Should().Be($"{lastBilledEnd:yyyy-MM-dd}");

        (await StartAsync(s, lastBilledEnd)).StatusCode.Should().Be(HttpStatusCode.NoContent, "coi như trả phòng từ ngày cuối kỳ đã chốt");
        (await _client.GetAsync($"/api/v1/invoices/{phantom}", s.Token)).StatusCode.Should().Be(HttpStatusCode.NotFound, "nháp kỳ ảo bị xóa");
        preview = await PreviewAsync(s);
        preview.GetProperty("finalInvoiceRequired").GetBoolean().Should().BeFalse("khu thu sau, kỳ cuối đã có hóa đơn tháng");
        var phantomCharge = preview.GetProperty("pendingCharges").EnumerateArray().Single().GetProperty("id").GetGuid();
        preview.GetProperty("pendingChargeIdsAfterEndDate").EnumerateArray().Select(x => x.GetGuid())
            .Should().Equal([phantomCharge], "khoản của kỳ ảo về chờ, gắn nhãn để chủ trọ xem lại");
        (await _client.PostJsonAsync($"/api/v1/room-charges/{phantomCharge}/cancel", new { reason = "Kỳ ảo" }, s.Token))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        preview.GetProperty("blockers").EnumerateArray().Should().BeEmpty();

        (await _client.PostJsonAsync($"/api/v1/contracts/{s.ContractId}/liquidation/complete", new { settlement = "CollectAll" }, s.Token))
            .StatusCode.Should().Be(HttpStatusCode.NoContent, "không cần hóa đơn trả phòng / chỉ số cuối");
        var contract = await (await _client.GetAsync($"/api/v1/contracts/{s.ContractId}", s.Token)).ReadAsync<JsonElement>();
        contract.GetProperty("status").GetString().Should().Be("Ended");
    }

    [Fact]
    public async Task Prepaid_MoveOutStillNeedsInvoiceForLastElectricity_RefundSuggestionCappedByWhatTenantActuallyPaid()
    {
        var s = await ArrangeAsync("Prepaid");
        var february = s.Start.AddMonths(1);
        (await GenerateAsync(s, s.Start)).GetProperty("created").GetInt32().Should().Be(1);
        await FinalizeAsync(s, Id(await InvoiceForAsync(s, s.Start)));
        (await SaveReadingAsync(s, s.Start, 150)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await GenerateAsync(s, february)).GetProperty("created").GetInt32().Should().Be(1);
        var feb = await FinalizeAsync(s, Id(await InvoiceForAsync(s, february)));
        Task PayAsync(decimal amount) => _client.PostJsonAsync($"/api/v1/contracts/{s.ContractId}/payments",
            new { amount, method = "Cash", paidAt = s.Today, invoiceId = Id(feb) }, s.Token);
        async Task<JsonElement?> OverpaidAsync(Guid finalId, string charge)
        {
            (await ChargeAsync(s, charge)).StatusCode.Should().Be(HttpStatusCode.NoContent);
            var issue = (await InvoiceAsync(s, finalId)).GetProperty("issues").EnumerateArray()
                .Where(i => i.GetProperty("code").GetString() == "RENT_OVERPAID").ToList();
            return issue.Count == 0 ? null : issue.Single();
        }

        (await PreviewAsync(s, february.AddMonths(1).AddDays(-1))).GetProperty("finalInvoiceRequired").GetBoolean()
            .Should().BeTrue("khu thu trước: điện kỳ cuối chưa tính");
        var days = DateTime.DaysInMonth(february.Year, february.Month);
        var overpaid = Rent - Money(Rent * 15 / days);
        (await StartAsync(s, february.AddDays(14))).StatusCode.Should().Be(HttpStatusCode.NoContent);
        var final = await FinalInvoiceAsync(s, 170);
        final.GetProperty("issues").EnumerateArray().Should().NotContain(i => i.GetProperty("code").GetString() == "RENT_OVERPAID", "trọn kỳ ⇒ không thừa");
        (await InvoiceAsync(s, Id(final))).GetProperty("finalPeriod").GetProperty("rentBilledInvoiceNo").GetString()
            .Should().Be(feb.GetProperty("summary").GetProperty("invoiceNo").GetString());

        var unpaid = (await OverpaidAsync(Id(final), "Daily"))!.Value;
        unpaid.GetProperty("amount").ValueKind.Should().Be(JsonValueKind.Null, "chưa thu đồng nào ⇒ không gợi ý hoàn");
        unpaid.GetProperty("message").GetString().Should().Contain("hủy");

        await PayAsync(1_000_000);
        (await OverpaidAsync(Id(final), "None"))!.Value.GetProperty("amount").GetDecimal().Should().Be(1_000_000, "không hoàn quá số đã thu");
        (await OverpaidAsync(Id(final), "Daily"))!.Value.GetProperty("amount").GetDecimal().Should().Be(Math.Min(overpaid, 1_000_000));

        await PayAsync(Total(feb) - 1_000_000);
        var suggestion = (await OverpaidAsync(Id(final), "Daily"))!.Value;
        suggestion.GetProperty("amount").GetDecimal().Should().Be(overpaid);
        suggestion.GetProperty("ref").GetGuid().Should().Be(Id(feb), "UI điền sẵn dòng Hoàn trả với hóa đơn nguồn");
        (await _client.PostJsonAsync($"/api/v1/invoices/{Id(final)}/manual-lines", new
        {
            type = "Refund", description = "Hoàn tiền phòng những ngày chưa ở", amount = overpaid, note = "Trả phòng 15/2", sourceInvoiceId = Id(feb)
        }, s.Token)).StatusCode.Should().Be(HttpStatusCode.OK, "số gợi ý luôn hoàn được");
        (await OverpaidAsync(Id(final), "Daily")).Should().BeNull("đã hoàn đủ ⇒ hết cảnh báo");
    }

    [Theory]
    [InlineData("Daily", false, "Daily")]
    [InlineData("FullPeriod", false, "None")]
    [InlineData("FullPeriod", true, "FullPeriod")]
    public async Task Resign_OldContractFinalPeriodSetSoBothContractsChargeOnePeriod(string propertyProration, bool atPeriodEnd, string expected)
    {
        var s = await ArrangeAsync(prorationMode: propertyProration);
        await BillFirstMonthAsync(s, 150);
        var handover = atPeriodEnd ? s.Start.AddMonths(2).AddDays(-1) : s.Start.AddMonths(1).AddDays(14);
        var resigned = await _client.PostJsonAsync($"/api/v1/contracts/{s.ContractId}/re-sign",
            new { handoverDate = handover, representativeRenterId = s.RenterId }, s.Token);
        resigned.StatusCode.Should().Be(HttpStatusCode.Created, await resigned.Content.ReadAsStringAsync());

        (await PreviewAsync(s)).GetProperty("finalPeriodCharge").GetString().Should().Be(expected, "người thuê không rời đi, HĐ mới tính tiếp từ X+1");
    }

    [Fact]
    public async Task EndedAtPeriodEndWithoutMoveOutInvoice_ChargeCannotBeUnsettled_NewTenantLowerHandoverCorrectsLastBilledReading()
    {
        var s = await ArrangeAsync();
        var end = s.Start.AddMonths(1).AddDays(-1);
        (await SaveReadingAsync(s, s.Start, 170, onAnchorDay: true)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await GenerateAsync(s, s.Start)).GetProperty("created").GetInt32().Should().Be(1);
        var january = await FinalizeAsync(s, Id(await InvoiceForAsync(s, s.Start)));
        (await _client.PostJsonAsync($"/api/v1/contracts/{s.ContractId}/payments",
            new { amount = Total(january), method = "Cash", paidAt = s.Today, invoiceId = Id(january) }, s.Token)).StatusCode.Should().Be(HttpStatusCode.Created);
        (await StartAsync(s, end)).StatusCode.Should().Be(HttpStatusCode.NoContent);

        var preview = await PreviewAsync(s);
        preview.GetProperty("finalInvoiceRequired").GetBoolean().Should().BeFalse();
        preview.GetProperty("missingFinalReadingMeterIds").EnumerateArray().Should().BeEmpty("không cần hóa đơn trả phòng ⇒ không báo thiếu số cuối");
        var key = await (await _client.PostJsonAsync($"/api/v1/rooms/{s.RoomId}/charges",
            new { kind = "Surcharge", description = "Mất chìa khóa", amount = 50_000, incurredOn = end, reason = "Phát hiện lúc trả phòng" }, s.Token))
            .ReadAsync<JsonElement>();
        (await _client.PostJsonAsync($"/api/v1/contracts/{s.ContractId}/liquidation/complete", new { pendingCharges = new { method = "Cash" } }, s.Token))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await (await _client.DeleteAsync($"/api/v1/room-charges/{key.GetProperty("id").GetGuid()}/settle", s.Token)).ReadProblemCodeAsync())
            .Should().Be("ROOM_CHARGE_NO_OPEN_INVOICE", "HĐ đã kết thúc — bỏ đánh dấu thì khoản treo, không còn nơi xử lý");

        var next = await _client.CreateContractAsync(s.Token, s.RoomId, await _client.CreateRenterAsync(s.Token), end.AddDays(1));
        object Handover(bool correct) => new { handoverReadings = new[] { new { meterId = s.MeterId, value = (decimal?)120, correctPreviousFinal = correct } } };
        (await (await _client.PostJsonAsync($"/api/v1/contracts/{next}/activate", Handover(false), s.Token)).ReadProblemCodeAsync())
            .Should().Be("HANDOVER_BELOW_PREVIOUS_FINAL", "số cuối của HĐ cũ là số chốt kỳ (không có số Final)");
        var activated = await _client.PostJsonAsync($"/api/v1/contracts/{next}/activate", Handover(true), s.Token);
        activated.StatusCode.Should().Be(HttpStatusCode.NoContent, await activated.Content.ReadAsStringAsync());
        (await ReadingsAsync(s)).Should().Contain(r => r.GetProperty("kind").GetString() == "Periodic" && r.GetProperty("value").GetDecimal() == 120);
        var charges = await (await _client.GetAsync($"/api/v1/rooms/{s.RoomId}/charges", s.Token)).ReadAsync<List<JsonElement>>();
        charges.Single(c => c.GetProperty("contractId").GetGuid() == s.ContractId && c.GetProperty("isSystem").GetBoolean())
            .GetProperty("amount").GetDecimal().Should().Be(50 * Price, "chênh 170 → 120 trên hóa đơn tháng cuối, chờ xử lý");
    }

    [Fact]
    public async Task NewTenantHandoverBelowFinal_WhilePreviousStillLiquidating_AsksThenRefusesToCorrect()
    {
        var s = await ArrangeAsync();
        (await StartAsync(s, s.Start.AddDays(9))).StatusCode.Should().Be(HttpStatusCode.NoContent);
        await FinalizeAsync(s, Id(await FinalInvoiceAsync(s, 170)));
        var next = await _client.CreateContractAsync(s.Token, s.RoomId, await _client.CreateRenterAsync(s.Token), s.Start.AddDays(10));
        object Handover(bool correct) => new { handoverReadings = new[] { new { meterId = s.MeterId, value = (decimal?)120, correctPreviousFinal = correct } } };

        (await (await _client.PostJsonAsync($"/api/v1/contracts/{next}/activate", Handover(false), s.Token)).ReadProblemCodeAsync())
            .Should().Be("HANDOVER_BELOW_PREVIOUS_FINAL");
        (await (await _client.PostJsonAsync($"/api/v1/contracts/{next}/activate", Handover(true), s.Token)).ReadProblemCodeAsync())
            .Should().Be("PREVIOUS_CONTRACT_NOT_ENDED", "HĐ cũ còn thanh lý ⇒ sửa số cuối trên HĐ cũ trước");
    }

    [Fact]
    public async Task Preview_ActiveContract_ListsUnbilledPeriodAndInvoicePastEndDate_WhichBlocksStartUntilDeleted()
    {
        var s = await ArrangeAsync();
        var month = s.Start.AddMonths(1);
        var end = month.AddDays(14);
        var preview = await PreviewAsync(s, end);
        preview.GetProperty("unbilledPeriods").EnumerateArray().Select(p => p.GetProperty("start").GetString())
            .Should().Equal($"{s.Start:yyyy-MM-dd}");
        preview.GetProperty("finalPeriod").GetProperty("start").GetString().Should().Be($"{month:yyyy-MM-dd}");

        await BillFirstMonthAsync(s, 150);
        (await SaveReadingAsync(s, month, 180)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await GenerateAsync(s, month)).GetProperty("created").GetInt32().Should().Be(1, "chưa báo trả phòng ⇒ hóa đơn tháng nguyên tháng");
        var phantom = await InvoiceForAsync(s, month);

        preview = await PreviewAsync(s, end);
        preview.GetProperty("unbilledPeriods").EnumerateArray().Should().BeEmpty();
        var fromEnd = preview.GetProperty("invoicesFromEndDate").EnumerateArray().Single();
        fromEnd.GetProperty("invoiceId").GetGuid().Should().Be(Id(phantom));
        fromEnd.GetProperty("mustCancel").GetBoolean().Should().BeTrue("khu thu sau đã tính điện tới cuối tháng, sau ngày trả phòng");
        (await (await StartAsync(s, end)).ReadProblemCodeAsync()).Should().Be("INVOICE_AFTER_END_DATE");

        (await _client.DeleteAsync($"/api/v1/invoices/{Id(phantom)}", s.Token)).IsSuccessStatusCode.Should().BeTrue();
        (await StartAsync(s, end)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        Amount(await FinalInvoiceAsync(s, 170), "Metered").Should().Be(20 * Price);
    }

    [Fact]
    public async Task Complete_OneClick_ConfirmsRefund_SettlesChargeAddedAfterFinal_ForfeitsDeposit()
    {
        var s = await ArrangeAsync(deposit: 3_000_000);
        var end = s.Start.AddDays(9);
        (await StartAsync(s, end, "Daily")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        var final = await FinalInvoiceAsync(s, 130);
        var credit = await _client.PostJsonAsync($"/api/v1/rooms/{s.RoomId}/charges",
            new { kind = "Credit", description = "Giảm do sửa nhà", amount = Rent, incurredOn = end, reason = "Sửa nhà 10 ngày" }, s.Token);
        credit.StatusCode.Should().Be(HttpStatusCode.Created, await credit.Content.ReadAsStringAsync());
        Total(await FinalizeAsync(s, Id(final))).Should().BeNegative();

        var key = await _client.PostJsonAsync($"/api/v1/rooms/{s.RoomId}/charges",
            new { kind = "Surcharge", description = "Mất chìa khóa", amount = 50_000, incurredOn = end, reason = "Phát hiện lúc trả phòng" }, s.Token);
        key.StatusCode.Should().Be(HttpStatusCode.Created, await key.Content.ReadAsStringAsync());
        var keyCharge = await key.ReadAsync<JsonElement>();
        keyCharge.GetProperty("status").GetString().Should().Be("Pending", "hóa đơn trả phòng đã chốt ⇒ khoản chưa xử lý");

        var preview = await PreviewAsync(s);
        preview.GetProperty("needsRefundDecision").GetBoolean().Should().BeTrue();
        preview.GetProperty("needsChargesDecision").GetBoolean().Should().BeTrue();
        preview.GetProperty("pendingCharges").EnumerateArray().Single().GetProperty("description").GetString().Should().Be("Mất chìa khóa");
        preview.GetProperty("blockers").EnumerateArray().Should().BeEmpty();

        var complete = $"/api/v1/contracts/{s.ContractId}/liquidation/complete";
        (await (await _client.PostJsonAsync(complete, null, s.Token)).ReadProblemCodeAsync()).Should().Be("REFUND_PENDING");
        var refund = new { refundedOn = s.Today, method = "Cash" };
        (await (await _client.PostJsonAsync(complete, new { refund }, s.Token)).ReadProblemCodeAsync()).Should().Be("PENDING_CHARGES");
        var pendingCharges = new { method = "Cash" };
        (await _client.PostJsonAsync(complete, new { refund, pendingCharges, deposit = new { action = "Forfeit" } }, s.Token))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest, "không trả cọc phải có lý do");
        (await _client.PostJsonAsync(complete,
                new { refund, pendingCharges, deposit = new { action = "Forfeit", note = "Trả phòng trước hạn, mất cọc theo HĐ" } }, s.Token))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);

        var contract = await (await _client.GetAsync($"/api/v1/contracts/{s.ContractId}", s.Token)).ReadAsync<JsonElement>();
        contract.GetProperty("status").GetString().Should().Be("Ended");
        contract.GetProperty("deposit").GetProperty("status").GetString().Should().Be("Forfeited");
        (await InvoiceAsync(s, Id(final))).GetProperty("refundedOn").GetString().Should().Be($"{s.Today:yyyy-MM-dd}");
        var charges = await (await _client.GetAsync($"/api/v1/rooms/{s.RoomId}/charges", s.Token)).ReadAsync<List<JsonElement>>();
        var settled = charges.Single(c => c.GetProperty("id").GetGuid() == keyCharge.GetProperty("id").GetGuid());
        settled.GetProperty("isSettled").GetBoolean().Should().BeTrue();
        settled.GetProperty("settledOn").GetString().Should().Be($"{s.Today:yyyy-MM-dd}");
    }

    [Fact]
    public async Task MoveOutNotice_SkipsMonthlyInvoice_ShowsLabel_WithdrawRestores()
    {
        var s = await ArrangeAsync();
        (await SaveReadingAsync(s, s.Start, 150)).StatusCode.Should().Be(HttpStatusCode.OK);
        var planned = s.Start.AddDays(14);
        (await _client.PostJsonAsync($"/api/v1/contracts/{s.ContractId}/notice", new { noticeDate = s.Start, plannedMoveOutDate = planned }, s.Token))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        var generated = await GenerateAsync(s, s.Start);
        generated.GetProperty("created").GetInt32().Should().Be(0);
        generated.GetProperty("skipped").EnumerateArray().Single().GetProperty("reason").GetString().Should().Be("MOVE_OUT_NOTICED");
        var rooms = await (await _client.GetAsync($"/api/v1/rooms?propertyId={s.PropertyId}", s.Token)).ReadAsync<JsonElement>();
        rooms.GetProperty("items")[0].GetProperty("currentContract").GetProperty("moveOutDate").GetString().Should().Be($"{planned:yyyy-MM-dd}");
        var sheet = await (await _client.GetAsync($"/api/v1/properties/{s.PropertyId}/meter-reading-sheet?billingMonth={s.Start:yyyy-MM}", s.Token))
            .ReadAsync<JsonElement>();
        sheet.GetProperty("rows")[0].GetProperty("moveOutDate").GetString().Should().Be($"{planned:yyyy-MM-dd}");

        (await _client.DeleteAsync($"/api/v1/contracts/{s.ContractId}/notice", s.Token)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await GenerateAsync(s, s.Start)).GetProperty("created").GetInt32().Should().Be(1, "rút báo trả phòng ⇒ lập lại được");
        (await (await _client.DeleteAsync($"/api/v1/contracts/{s.ContractId}/notice", s.Token)).ReadProblemCodeAsync()).Should().Be("NOTICE_NOT_GIVEN");
    }

    /// <summary>HĐ ở 10 ngày đầu kỳ đầu, số cuối gõ 170 (đúng 120), hóa đơn trả phòng đã thu đủ, đã hoàn tất thanh lý.</summary>
    private async Task<(Setup S, Guid FinalInvoiceId, Guid FinalReadingId)> EndedWithHighFinalAsync()
    {
        var s = await ArrangeAsync();
        (await StartAsync(s, s.Start.AddDays(9))).StatusCode.Should().Be(HttpStatusCode.NoContent);
        var final = await FinalizeAsync(s, Id(await FinalInvoiceAsync(s, 170)));
        (await _client.PostJsonAsync($"/api/v1/contracts/{s.ContractId}/payments",
            new { amount = Total(final), method = "Cash", paidAt = s.Today, invoiceId = Id(final) }, s.Token)).StatusCode.Should().Be(HttpStatusCode.Created);
        (await _client.PostJsonAsync($"/api/v1/contracts/{s.ContractId}/liquidation/complete", null, s.Token)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        var readingId = (await ReadingsAsync(s)).Single(r => r.GetProperty("kind").GetString() == "Final").GetProperty("id").GetGuid();
        return (s, Id(final), readingId);
    }

    private Task<HttpResponseMessage> AdjustAsync(Setup s, Guid readingId, decimal value, object? extra = null) =>
        _client.PostJsonAsync($"/api/v1/meter-readings/{readingId}/adjust", extra ?? new { value, reason = "Gõ nhầm số cuối" }, s.Token);

    [Fact]
    public async Task AfterEnd_LowerFinal_KeepsInvoice_DifferencePending_WaiveOrRefund_IncreaseRejected()
    {
        var (s, finalId, readingId) = await EndedWithHighFinalAsync();
        var before = Total(await InvoiceAsync(s, finalId));

        var pending = await AdjustAsync(s, readingId, 120);
        pending.StatusCode.Should().Be(HttpStatusCode.OK, await pending.Content.ReadAsStringAsync());
        var result = await pending.ReadAsync<JsonElement>();
        result.GetProperty("handling").GetString().Should().Be("AfterEnd");
        result.GetProperty("charge").GetProperty("amount").GetDecimal().Should().Be(50 * Price);
        result.GetProperty("charge").GetProperty("kind").GetString().Should().Be("Credit");
        result.GetProperty("charge").GetProperty("status").GetString().Should().Be("Pending");
        Total(result.GetProperty("invoice")).Should().Be(before, "hóa đơn trả phòng giữ nguyên");
        result.GetProperty("invoice").GetProperty("summary").GetProperty("isAdjusted").GetBoolean().Should().BeTrue();
        result.GetProperty("invoice").GetProperty("summary").GetProperty("needsResend").GetBoolean().Should().BeFalse();

        var waived = await _client.PostJsonAsync($"/api/v1/room-charges/{result.GetProperty("charge").GetProperty("id").GetGuid()}/waive", null, s.Token);
        waived.StatusCode.Should().Be(HttpStatusCode.OK, await waived.Content.ReadAsStringAsync());
        (await waived.ReadAsync<JsonElement>()).GetProperty("cancelReason").GetString().Should().Be("Không trả lại");

        var refunded = await (await AdjustAsync(s, readingId, 110, new
        {
            value = 110, reason = "Kiểm lại lần 2", settlement = new { date = s.Today, method = "BankTransfer" }
        })).ReadAsync<JsonElement>();
        refunded.GetProperty("charge").GetProperty("amount").GetDecimal().Should().Be(60 * Price, "luôn so với số lúc chốt");
        refunded.GetProperty("charge").GetProperty("isSettled").GetBoolean().Should().BeTrue();
        var refundedId = refunded.GetProperty("charge").GetProperty("id").GetGuid();
        (await _client.DeleteAsync($"/api/v1/room-charges/{refundedId}/settle", s.Token)).StatusCode.Should().Be(HttpStatusCode.OK,
            "sửa nhầm \"Đã trả lại\" vẫn được sau khi HĐ kết thúc");
        (await _client.PostJsonAsync($"/api/v1/room-charges/{refundedId}/settle", new { settledOn = s.Today, method = "BankTransfer" }, s.Token))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        (await (await AdjustAsync(s, readingId, 105)).ReadProblemCodeAsync()).Should().Be("ADJUSTMENT_ALREADY_BILLED");
        (await (await AdjustAsync(s, readingId, 130)).ReadProblemCodeAsync()).Should().Be("ADJUSTMENT_AFTER_END_INCREASE");
        (await AdjustAsync(s, readingId, 100, new { value = 100, reason = "x", settlement = new { date = s.Today, method = "Cash" }, notReturned = true }))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task NewTenantHandoverBelowPreviousFinal_Asks_ThenCorrectsOldFinalWithPendingDifference()
    {
        var (s, _, readingId) = await EndedWithHighFinalAsync();
        var renter = await _client.CreateRenterAsync(s.Token);
        var next = await _client.CreateContractAsync(s.Token, s.RoomId, renter, s.Start.AddDays(10));
        object Handover(bool correct) => new { handoverReadings = new[] { new { meterId = s.MeterId, value = (decimal?)120, correctPreviousFinal = correct } } };

        var asked = await _client.PostJsonAsync($"/api/v1/contracts/{next}/activate", Handover(false), s.Token);
        (await asked.ReadProblemCodeAsync()).Should().Be("HANDOVER_BELOW_PREVIOUS_FINAL");
        var activated = await _client.PostJsonAsync($"/api/v1/contracts/{next}/activate", Handover(true), s.Token);
        activated.StatusCode.Should().Be(HttpStatusCode.NoContent, await activated.Content.ReadAsStringAsync());

        var readings = await ReadingsAsync(s);
        readings.Single(r => r.GetProperty("id").GetGuid() == readingId).GetProperty("value").GetDecimal().Should().Be(120);
        readings.Should().Contain(r => r.GetProperty("kind").GetString() == "Handover" && r.GetProperty("value").GetDecimal() == 120);
        var charges = await (await _client.GetAsync($"/api/v1/rooms/{s.RoomId}/charges", s.Token)).ReadAsync<List<JsonElement>>();
        var difference = charges.Single(c => c.GetProperty("contractId").GetGuid() == s.ContractId && c.GetProperty("isSystem").GetBoolean());
        difference.GetProperty("amount").GetDecimal().Should().Be(50 * Price);
        difference.GetProperty("status").GetString().Should().Be("Pending", "chờ chủ trọ chọn đã trả lại / không trả lại");
    }
}
