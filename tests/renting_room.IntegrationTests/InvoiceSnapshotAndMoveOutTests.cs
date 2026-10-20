using System.Net;
using System.Text.Json;
using renting_room.IntegrationTests.Infrastructure;

namespace renting_room.IntegrationTests;

/// <summary>
/// 20/10/2026 (Lượt 1): bộ giá lưu trên dòng (M07 BL-BR-39), trạng thái hóa đơn trong lưới chỉ số (M06 §3.4), hóa đơn trả phòng được âm khi trừ
/// khoản bù (BL-BR-10), chốt hóa đơn khóa công tơ (BL-BR-12).
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class InvoiceSnapshotAndMoveOutTests(ApiFactory factory)
{
    private readonly HttpClient _client = factory.CreateClient();

    private sealed record Setup(string Token, Guid PropertyId, Guid ContractId, Guid MeterId, Guid ElectricityId, DateOnly Month);

    /// <summary>Khu thu sau chốt ngày 1, HĐ từ đầu tháng trước (chỉ số cuối kỳ ghi ngày 1 tháng này), công tơ điện nhận phòng ở 100.</summary>
    private async Task<Setup> ArrangeAsync(object price)
    {
        var token = (await _client.CreateActiveOwnerAsync()).Tokens.AccessToken;
        var today = TestData.Today(factory);
        var month = new DateOnly(today.Year, today.Month, 1).AddMonths(-1);
        var propertyId = await _client.CreatePropertyAsync(token, anchorDay: 1, chargeMode: "Postpaid");
        var electricity = await _client.FeeIdAsync(token, propertyId, "Điện");
        (await _client.PostJsonAsync($"/api/v1/fee-types/{electricity}/prices", price, token)).IsSuccessStatusCode.Should().BeTrue();
        var roomId = await _client.CreateRoomAsync(token, propertyId);
        var meterId = await _client.InstallMeterAsync(token, roomId, electricity, month.AddDays(-1), 100);
        var contractId = await _client.CreateContractAsync(token, roomId, await _client.CreateRenterAsync(token), month);
        (await _client.PostJsonAsync($"/api/v1/contracts/{contractId}/activate",
            new { handoverReadings = new[] { new { meterId, value = (decimal?)null } } }, token)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        return new Setup(token, propertyId, contractId, meterId, electricity, month);
    }

    private Task<HttpResponseMessage> SaveAsync(Setup s, decimal value) =>
        _client.PutJsonAsync($"/api/v1/properties/{s.PropertyId}/meter-readings", new
        {
            readings = new[] { new { meterId = s.MeterId, contractId = s.ContractId, closingPeriodStart = s.Month, readingDate = s.Month.AddMonths(1), value } }
        }, s.Token);

    private async Task<JsonElement> DraftAsync(Setup s)
    {
        await _client.PostJsonAsync("/api/v1/invoices/generate", new { propertyId = s.PropertyId, billingMonth = $"{s.Month:yyyy-MM}" }, s.Token);
        var page = await (await _client.GetAsync($"/api/v1/invoices?contractId={s.ContractId}", s.Token)).ReadAsync<JsonElement>();
        return await (await _client.GetAsync($"/api/v1/invoices/{page.GetProperty("items")[0].GetProperty("id").GetGuid()}", s.Token)).ReadAsync<JsonElement>();
    }

    private static JsonElement Metered(JsonElement invoice) =>
        invoice.GetProperty("lines").EnumerateArray().Single(l => l.GetProperty("type").GetString() == "Metered");

    private async Task<JsonElement> SheetRowAsync(Setup s) =>
        (await (await _client.GetAsync($"/api/v1/properties/{s.PropertyId}/meter-reading-sheet?billingMonth={s.Month:yyyy-MM}", s.Token))
            .ReadAsync<JsonElement>()).GetProperty("rows")[0];

    [Fact]
    public async Task PriceSnapshot_TieredSplitPerTier_FollowsNewPriceWithinPeriodOnly()
    {
        var s = await ArrangeAsync(new
        {
            effectiveFrom = new DateOnly(2026, 1, 1), tiers = new object[] { new { upTo = 50, price = 2000 }, new { upTo = (int?)null, price = 3000 } }
        });
        (await SaveAsync(s, 180)).StatusCode.Should().Be(HttpStatusCode.OK);

        var price = Metered(await DraftAsync(s)).GetProperty("price");
        price.GetProperty("source").GetString().Should().Be("Catalog");
        price.GetProperty("effectiveFrom").GetString().Should().Be("2026-01-01");
        var tiers = price.GetProperty("tiers").EnumerateArray().ToList();
        tiers.Select(t => t.GetProperty("quantity").GetDecimal()).Should().Equal(50, 30);
        tiers.Sum(t => t.GetProperty("amount").GetDecimal()).Should().Be(50 * 2000 + 30 * 3000);

        var inPeriod = s.Month.AddDays(5);
        await _client.PostJsonAsync($"/api/v1/fee-types/{s.ElectricityId}/prices", new { effectiveFrom = inPeriod, unitPrice = 3500 }, s.Token);
        await _client.PostJsonAsync($"/api/v1/fee-types/{s.ElectricityId}/prices", new { effectiveFrom = s.Month.AddMonths(1).AddDays(3), unitPrice = 4000 }, s.Token);
        await _client.PostJsonAsync("/api/v1/invoices/recalculate", new { propertyId = s.PropertyId, staleOnly = true }, s.Token);

        var recalculated = Metered(await DraftAsync(s));
        recalculated.GetProperty("price").GetProperty("effectiveFrom").GetString().Should().Be($"{inPeriod:yyyy-MM-dd}", "bản giá sau ngày cuối kỳ không áp");
        recalculated.GetProperty("price").GetProperty("tiers").ValueKind.Should().Be(JsonValueKind.Null);
        recalculated.GetProperty("amount").GetDecimal().Should().Be(80 * 3500);
    }

    [Fact]
    public async Task ReadingSheet_ShowsInvoiceStatusOfEachRow()
    {
        var s = await ArrangeAsync(new { effectiveFrom = new DateOnly(2026, 1, 1), unitPrice = 3800 });
        (await SheetRowAsync(s)).GetProperty("invoice").ValueKind.Should().Be(JsonValueKind.Null, "chưa nhập, chưa có hóa đơn");

        (await SaveAsync(s, 150)).StatusCode.Should().Be(HttpStatusCode.OK);
        var draft = await DraftAsync(s);
        var row = await SheetRowAsync(s);
        row.GetProperty("invoice").GetProperty("status").GetString().Should().Be("Draft");
        row.GetProperty("invoice").GetProperty("id").GetGuid().Should().Be(draft.GetProperty("summary").GetProperty("id").GetGuid());

        (await _client.PostJsonAsync($"/api/v1/invoices/{draft.GetProperty("summary").GetProperty("id").GetGuid()}/finalize", null, s.Token))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        (await SheetRowAsync(s)).GetProperty("invoice").GetProperty("status").GetString().Should().Be("Finalized");
    }

    [Fact]
    public async Task FinalizeConcurrentWithReadingCorrection_InvoiceNeverDisagreesWithReading()
    {
        var s = await ArrangeAsync(new { effectiveFrom = new DateOnly(2026, 1, 1), unitPrice = 3800 });
        (await SaveAsync(s, 150)).StatusCode.Should().Be(HttpStatusCode.OK);
        var invoiceId = (await DraftAsync(s)).GetProperty("summary").GetProperty("id").GetGuid();
        var readingId = (await (await _client.GetAsync($"/api/v1/meters/{s.MeterId}/readings", s.Token)).ReadAsync<List<JsonElement>>())
            .Single(r => r.GetProperty("kind").GetString() == "Periodic").GetProperty("id").GetGuid();

        var finalize = _client.PostJsonAsync($"/api/v1/invoices/{invoiceId}/finalize", null, s.Token);
        var correct = _client.PutJsonAsync($"/api/v1/meter-readings/{readingId}", new { value = 160 }, s.Token);
        await Task.WhenAll(finalize, correct);

        var reading = (await (await _client.GetAsync($"/api/v1/meters/{s.MeterId}/readings", s.Token)).ReadAsync<List<JsonElement>>())
            .Single(r => r.GetProperty("id").GetGuid() == readingId).GetProperty("value").GetDecimal();
        var invoice = await (await _client.GetAsync($"/api/v1/invoices/{invoiceId}", s.Token)).ReadAsync<JsonElement>();
        if (correct.Result.IsSuccessStatusCode)
            reading.Should().Be(160);
        else
            (await correct.Result.ReadProblemCodeAsync()).Should().Be("READING_LOCKED", "chốt trước ⇒ chỉ số đã khóa");
        if (finalize.Result.IsSuccessStatusCode)
            Metered(invoice).GetProperty("quantity").GetDecimal().Should().Be(reading - 100, "hóa đơn chốt luôn khớp chỉ số đang lưu");
    }

    [Fact]
    public async Task MoveOutInvoice_CreditLargerThanInvoice_GoesNegative_RefundPendingBlocksCompletionUntilConfirmed()
    {
        var token = (await _client.CreateActiveOwnerAsync()).Tokens.AccessToken;
        var today = TestData.Today(factory);
        var month = new DateOnly(today.Year, today.Month, 1);
        var (_, roomId, _, contractId) = await _client.CreateActiveContractAsync(token, month, anchorDay: 1);
        (await _client.PostJsonAsync($"/api/v1/contracts/{contractId}/liquidation/start",
            new { actualEndDate = month, reason = "MutualAgreement" }, token)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        var draft = await _client.PostJsonAsync($"/api/v1/contracts/{contractId}/final-invoice", new { finalReadings = Array.Empty<object>() }, token);
        draft.StatusCode.Should().Be(HttpStatusCode.Created, await draft.Content.ReadAsStringAsync());
        var moveOutId = (await draft.ReadAsync<JsonElement>()).GetProperty("summary").GetProperty("id").GetGuid();

        var credit = await _client.PostJsonAsync($"/api/v1/rooms/{roomId}/charges", new
        {
            kind = "Credit", description = "Giảm do cúp nước", amount = 1_000_000, incurredOn = month, reason = "Cúp nước 3 ngày"
        }, token);
        credit.StatusCode.Should().Be(HttpStatusCode.Created, await credit.Content.ReadAsStringAsync());
        (await credit.ReadAsync<JsonElement>()).GetProperty("status").GetString().Should().Be("OnDraft", "hóa đơn trả phòng nhận khoản bù dù âm");

        (await _client.PostJsonAsync($"/api/v1/invoices/{moveOutId}/finalize", null, token)).StatusCode.Should().Be(HttpStatusCode.OK);
        var finalized = await (await _client.GetAsync($"/api/v1/invoices/{moveOutId}", token)).ReadAsync<JsonElement>();
        finalized.GetProperty("summary").GetProperty("totalAmount").GetDecimal().Should().BeNegative();
        finalized.GetProperty("summary").GetProperty("paymentStatus").GetString().Should().Be("RefundPending");

        (await (await _client.PostJsonAsync($"/api/v1/contracts/{contractId}/liquidation/complete", null, token)).ReadProblemCodeAsync())
            .Should().Be("REFUND_PENDING");
        (await _client.PostJsonAsync($"/api/v1/invoices/{moveOutId}/refund", new { refundedOn = today, method = "Cash" }, token))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        (await _client.PostJsonAsync($"/api/v1/contracts/{contractId}/liquidation/complete", null, token))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);
    }
}
