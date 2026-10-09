using System.Net;
using System.Text.Json;
using renting_room.IntegrationTests.Infrastructure;

namespace renting_room.IntegrationTests;

/// <summary>PR-BR-09 (cài đặt kỳ thu của khu), K4 (đổi ngày chốt khi đã có phiếu — BL-BR-28), K5 ("Tính tiền từ ngày" — C-05).</summary>
[Collection(ApiCollection.Name)]
public sealed class PropertyBillingTests(ApiFactory factory)
{
    private readonly HttpClient _client = factory.CreateClient();

    private async Task<JsonElement> InvoiceAsync(string token, Guid contractId, DateOnly month)
    {
        var page = await (await _client.GetAsync($"/api/v1/invoices?contractId={contractId}&billingMonth={month:yyyy-MM}", token)).ReadAsync<JsonElement>();
        var id = page.GetProperty("items")[0].GetProperty("id").GetGuid();
        return await (await _client.GetAsync($"/api/v1/invoices/{id}", token)).ReadAsync<JsonElement>();
    }

    private async Task GenerateAsync(string token, Guid propertyId, DateOnly month) =>
        (await _client.PostJsonAsync("/api/v1/invoices/generate", new { propertyId, billingMonth = $"{month:yyyy-MM}" }, token))
            .StatusCode.Should().Be(HttpStatusCode.OK);

    private static object Billing(int anchorDay, int? adjust = null) =>
        new { anchorDay, chargeMode = "Prepaid", paymentDueDays = 5, prorationMode = "Daily", noticeDays = 30, transitionAdjustDays = adjust };

    [Fact]
    public async Task ChangeAnchor_AfterInvoices_CreatesTransitionPeriod_WithChosenExtraDays()
    {
        var owner = await _client.CreateActiveOwnerAsync();
        var token = owner.Tokens.AccessToken;
        var today = TestData.Today(factory);
        var month1 = new DateOnly(today.Year, today.Month, 1).AddMonths(-1);
        var month2 = month1.AddMonths(1);
        var (propertyId, _, _, contractId) = await _client.CreateActiveContractAsync(token, month1, anchorDay: 1);

        await GenerateAsync(token, propertyId, month1);
        (await (await _client.PutJsonAsync($"/api/v1/properties/{propertyId}/billing", Billing(5), token)).ReadProblemCodeAsync())
            .Should().Be("BILLING_SETTINGS_DRAFT_INVOICES");
        var first = await InvoiceAsync(token, contractId, month1);
        (await _client.PostJsonAsync($"/api/v1/invoices/{first.GetProperty("summary").GetProperty("id").GetGuid()}/finalize", null, token))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        var preview = await (await _client.GetAsync($"/api/v1/properties/{propertyId}/billing/preview?anchorDay=5&chargeMode=Prepaid", token))
            .ReadAsync<JsonElement>();
        var baseDays = DateTime.DaysInMonth(month2.Year, month2.Month);
        preview.GetProperty("effectiveFrom").GetString().Should().Be($"{month2:yyyy-MM-dd}");
        preview.GetProperty("transitionEnd").GetString().Should().Be($"{month2.AddMonths(1).AddDays(3):yyyy-MM-dd}");
        preview.GetProperty("deviationDays").GetInt32().Should().Be(4);
        preview.GetProperty("suggestedAdjustDays").GetInt32().Should().Be(4);
        preview.GetProperty("rooms")[0].GetProperty("suggestedAmount").GetDecimal().Should().Be(Math.Round(3_500_000m * 4 / baseDays, 0, MidpointRounding.AwayFromZero));

        (await (await _client.PutJsonAsync($"/api/v1/properties/{propertyId}/billing", Billing(5, adjust: 5), token)).ReadProblemCodeAsync())
            .Should().Be("TRANSITION_ADJUST_OUT_OF_RANGE");
        var saved = await (await _client.PutJsonAsync($"/api/v1/properties/{propertyId}/billing", Billing(5, adjust: 2), token)).ReadAsync<JsonElement>();
        saved.GetProperty("billing").GetProperty("anchorDay").GetInt32().Should().Be(5);
        saved.GetProperty("billing").GetProperty("changes")[0].GetProperty("adjustDays").GetInt32().Should().Be(2);

        await GenerateAsync(token, propertyId, month2);
        var transition = await InvoiceAsync(token, contractId, month2);
        transition.GetProperty("summary").GetProperty("periodEnd").GetString().Should().Be($"{month2.AddMonths(1).AddDays(3):yyyy-MM-dd}");
        var rent = transition.GetProperty("lines").EnumerateArray().Single(l => l.GetProperty("type").GetString() == "Rent");
        rent.GetProperty("amount").GetDecimal().Should().Be(Math.Round(3_500_000m * (1 + 2m / baseDays), 0, MidpointRounding.AwayFromZero));
        rent.GetProperty("description").GetString().Should().Contain("+ 2 ngày");
    }

    [Fact]
    public async Task BillingStartDate_SkipsDaysBefore_FirstPeriodIsNotMerged_AndBelongsToTheKhuMonth()
    {
        var owner = await _client.CreateActiveOwnerAsync();
        var token = owner.Tokens.AccessToken;
        var today = TestData.Today(factory);
        var previous = new DateOnly(today.Year, today.Month, 1).AddMonths(-1);
        var propertyId = await _client.CreatePropertyAsync(token, anchorDay: 5);
        var roomId = await _client.CreateRoomAsync(token, propertyId);
        var renter = await _client.CreateRenterAsync(token);
        var billingStart = previous.AddDays(9); // ngày 10 tháng trước — HĐ nhập từ sổ cũ bắt đầu ngày 3
        var created = await _client.PostJsonAsync("/api/v1/contracts", new
        {
            roomId,
            contract = new
            {
                representativeRenterId = renter, startDate = previous.AddDays(2), billingStartDate = billingStart,
                monthlyRent = 3_100_000, depositAmount = 0, occupants = new[] { new { renterId = renter } }
            }
        }, token);
        created.StatusCode.Should().Be(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        var contractId = await created.ReadIdAsync();
        (await _client.PostJsonAsync($"/api/v1/contracts/{contractId}/activate", null, token)).StatusCode.Should().Be(HttpStatusCode.NoContent);

        var periods = await (await _client.GetAsync($"/api/v1/contracts/{contractId}/billing-periods", token)).ReadAsync<JsonElement>();
        periods[0].GetProperty("start").GetString().Should().Be($"{billingStart:yyyy-MM-dd}");
        periods[0].GetProperty("end").GetString().Should().Be($"{previous.AddMonths(1).AddDays(3):yyyy-MM-dd}");
        periods[0].GetProperty("billingMonth").GetString().Should().Be($"{previous:yyyy-MM}");

        await GenerateAsync(token, propertyId, previous);
        var invoice = await InvoiceAsync(token, contractId, previous);
        invoice.GetProperty("summary").GetProperty("periodStart").GetString().Should().Be($"{billingStart:yyyy-MM-dd}", "những ngày trước \"Tính tiền từ ngày\" không tính");
        var standardDays = previous.AddMonths(1).AddDays(4).DayNumber - previous.AddDays(4).DayNumber;
        var days = previous.AddMonths(1).AddDays(4).DayNumber - billingStart.DayNumber;
        invoice.GetProperty("lines").EnumerateArray().Single(l => l.GetProperty("type").GetString() == "Rent").GetProperty("amount").GetDecimal()
            .Should().Be(Math.Round(3_100_000m * days / standardDays, 0, MidpointRounding.AwayFromZero));
    }
}
