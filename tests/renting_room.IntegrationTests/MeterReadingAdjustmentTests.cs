using System.Net;
using System.Text.Json;
using renting_room.IntegrationTests.Infrastructure;

namespace renting_room.IntegrationTests;

/// <summary>M06 MT-UC-09 / MT-BR-19 (18/10/2026): điều chỉnh chỉ số đã chốt — sửa chỉ số + tự tạo khoản bù / phụ thu tiền chênh vào phiếu kỳ sau.</summary>
[Collection(ApiCollection.Name)]
public sealed class MeterReadingAdjustmentTests(ApiFactory factory)
{
    private readonly HttpClient _client = factory.CreateClient();

    private sealed record Setup(string Token, Guid PropertyId, Guid ContractId, Guid MeterId, DateOnly Previous, DateOnly Month, DateOnly Today);

    /// <summary>Khu thu sau chốt ngày 1, HĐ từ đầu tháng trước, công tơ điện nhận phòng ở <paramref name="handover"/>.</summary>
    private async Task<Setup> ArrangeAsync(decimal handover, object price)
    {
        var owner = await _client.CreateActiveOwnerAsync();
        var token = owner.Tokens.AccessToken;
        var today = TestData.Today(factory);
        var month = new DateOnly(today.Year, today.Month, 1);
        var previous = month.AddMonths(-1);
        var propertyId = await _client.CreatePropertyAsync(token, anchorDay: 1, chargeMode: "Postpaid");
        var electricity = await _client.FeeIdAsync(token, propertyId, "Điện");
        (await _client.PostJsonAsync($"/api/v1/fee-types/{electricity}/prices", price, token)).IsSuccessStatusCode.Should().BeTrue();
        var roomId = await _client.CreateRoomAsync(token, propertyId);
        var meterId = await _client.InstallMeterAsync(token, roomId, electricity, previous.AddDays(-1), handover);
        var contractId = await _client.CreateContractAsync(token, roomId, await _client.CreateRenterAsync(token), previous);
        (await _client.PostJsonAsync($"/api/v1/contracts/{contractId}/activate",
            new { handoverReadings = new[] { new { meterId, value = (decimal?)null } } }, token)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        return new Setup(token, propertyId, contractId, meterId, previous, month, today);
    }

    private Task<HttpResponseMessage> SaveAsync(Setup s, DateOnly closing, DateOnly on, decimal value) =>
        _client.PutJsonAsync($"/api/v1/properties/{s.PropertyId}/meter-readings",
            new { readings = new[] { new { meterId = s.MeterId, contractId = s.ContractId, closingPeriodStart = closing, readingDate = on, value } } }, s.Token);

    private async Task<Guid> PeriodicIdAsync(Setup s, decimal value) =>
        (await (await _client.GetAsync($"/api/v1/meters/{s.MeterId}/readings", s.Token)).ReadAsync<List<JsonElement>>())
        .Single(r => r.GetProperty("kind").GetString() == "Periodic" && r.GetProperty("value").GetDecimal() == value).GetProperty("id").GetGuid();

    private async Task<JsonElement> InvoiceAsync(Setup s, DateOnly billingMonth)
    {
        (await (await _client.PostJsonAsync("/api/v1/invoices/generate", new { propertyId = s.PropertyId, billingMonth = $"{billingMonth:yyyy-MM}" }, s.Token))
            .ReadAsync<JsonElement>()).GetProperty("created").GetInt32().Should().Be(1);
        var page = await (await _client.GetAsync($"/api/v1/invoices?contractId={s.ContractId}&billingMonth={billingMonth:yyyy-MM}", s.Token)).ReadAsync<JsonElement>();
        return await (await _client.GetAsync($"/api/v1/invoices/{page.GetProperty("items")[0].GetProperty("id").GetGuid()}", s.Token)).ReadAsync<JsonElement>();
    }

    private async Task FinalizeAsync(Setup s, JsonElement invoice)
    {
        var finalized = await _client.PostJsonAsync($"/api/v1/invoices/{invoice.GetProperty("summary").GetProperty("id").GetGuid()}/finalize", null, s.Token);
        finalized.StatusCode.Should().Be(HttpStatusCode.OK, await finalized.Content.ReadAsStringAsync());
    }

    private Task<HttpResponseMessage> AdjustAsync(Setup s, Guid readingId, decimal value) =>
        _client.PostJsonAsync($"/api/v1/meter-readings/{readingId}/adjust", new { value, reason = "Gõ nhầm chỉ số khi ghi" }, s.Token);

    [Fact]
    public async Task Overcharged_FinalizedAndPaid_AdjustCreatesCreditOnNextInvoice_NextMonthStartsFromCorrectValue()
    {
        var s = await ArrangeAsync(1250, new { effectiveFrom = new DateOnly(2026, 1, 1), unitPrice = 3800 });
        (await SaveAsync(s, s.Previous, s.Month, 1388)).StatusCode.Should().Be(HttpStatusCode.OK); // đúng là 1.338
        var wrongId = await PeriodicIdAsync(s, 1388);
        (await (await AdjustAsync(s, wrongId, 1338)).ReadProblemCodeAsync()).Should().Be("READING_NOT_LOCKED", "chưa chốt thì sửa trực tiếp");

        var october = await InvoiceAsync(s, s.Previous);
        await FinalizeAsync(s, october);
        var octoberId = october.GetProperty("summary").GetProperty("id").GetGuid();
        (await _client.PostJsonAsync($"/api/v1/contracts/{s.ContractId}/payments",
            new { amount = october.GetProperty("summary").GetProperty("totalAmount").GetDecimal(), method = "Cash", paidAt = s.Today, invoiceId = octoberId },
            s.Token)).StatusCode.Should().Be(HttpStatusCode.Created);

        (await (await _client.PutJsonAsync($"/api/v1/meter-readings/{wrongId}", new { value = 1338 }, s.Token)).ReadProblemCodeAsync())
            .Should().Be("READING_LOCKED");
        (await (await AdjustAsync(s, wrongId, 1388)).ReadProblemCodeAsync()).Should().Be("READING_UNCHANGED");

        var adjusted = await AdjustAsync(s, wrongId, 1338);
        adjusted.StatusCode.Should().Be(HttpStatusCode.OK, await adjusted.Content.ReadAsStringAsync());
        var result = await adjusted.ReadAsync<JsonElement>();
        result.GetProperty("reading").GetProperty("value").GetDecimal().Should().Be(1338);
        var charge = result.GetProperty("charge");
        charge.GetProperty("kind").GetString().Should().Be("Credit");
        charge.GetProperty("amount").GetDecimal().Should().Be(50 * 3800, "thu dư 50 kWh × 3.800đ");
        charge.GetProperty("status").GetString().Should().Be("Pending", "chưa có nháp tháng sau — chờ vào phiếu");
        var chargeId = charge.GetProperty("id").GetGuid();

        // Phiếu tháng 10 giữ nguyên làm chứng từ; tháng 11 nhập 1.380 được, tính 42 kWh và trừ khoản bù.
        var unchanged = await (await _client.GetAsync($"/api/v1/invoices/{octoberId}", s.Token)).ReadAsync<JsonElement>();
        unchanged.GetProperty("summary").GetProperty("totalAmount").GetDecimal().Should().Be(october.GetProperty("summary").GetProperty("totalAmount").GetDecimal());
        (await SaveAsync(s, s.Month, s.Today, 1380)).StatusCode.Should().Be(HttpStatusCode.OK);
        var november = await InvoiceAsync(s, s.Month);
        november.GetProperty("lines").EnumerateArray().Single(l => l.GetProperty("type").GetString() == "Metered")
            .GetProperty("quantity").GetDecimal().Should().Be(42);
        november.GetProperty("lines").EnumerateArray().Should().Contain(l => l.GetProperty("type").GetString() == "ManualDiscount"
            && l.GetProperty("roomChargeId").GetGuid() == chargeId && l.GetProperty("amount").GetDecimal() == -190_000);

        // Kỳ sau đã chốt từ chỉ số này ⇒ không điều chỉnh nữa.
        await FinalizeAsync(s, november);
        (await (await AdjustAsync(s, wrongId, 1330)).ReadProblemCodeAsync()).Should().Be("READING_NEXT_PERIOD_FINALIZED");
    }

    [Fact]
    public async Task Undercharged_TieredPrice_AdjustCreatesSurchargeForTierDifference()
    {
        var s = await ArrangeAsync(100, new
        {
            effectiveFrom = new DateOnly(2026, 1, 1), tiers = new object[] { new { upTo = 50, price = 2000 }, new { upTo = (int?)null, price = 3000 } }
        });
        (await SaveAsync(s, s.Previous, s.Month, 150)).StatusCode.Should().Be(HttpStatusCode.OK); // đúng là 180
        await FinalizeAsync(s, await InvoiceAsync(s, s.Previous)); // 50 kWh × 2.000 = 100.000

        var adjusted = await AdjustAsync(s, await PeriodicIdAsync(s, 150), 180);
        adjusted.StatusCode.Should().Be(HttpStatusCode.OK, await adjusted.Content.ReadAsStringAsync());
        var charge = (await adjusted.ReadAsync<JsonElement>()).GetProperty("charge");
        charge.GetProperty("kind").GetString().Should().Be("Surcharge");
        charge.GetProperty("amount").GetDecimal().Should().Be(50 * 2000 + 30 * 3000 - 100_000, "80 kWh tính bậc trên tổng của dòng, trừ số đã tính");
    }
}
