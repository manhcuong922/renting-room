using System.Net;
using System.Text.Json;
using renting_room.IntegrationTests.Infrastructure;

namespace renting_room.IntegrationTests;

/// <summary>M08 F1 (PM-UC-18): phiếu hiện "Nợ các kỳ trước" + "Tổng cần thanh toán" (chỉ hiển thị); thu theo tổng vẫn trừ phiếu cũ nhất trước.</summary>
[Collection(ApiCollection.Name)]
public sealed class PreviousDebtTests(ApiFactory factory)
{
    private const decimal Rent = 3_000_000;
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task NewInvoice_ShowsUnpaidPreviousMonth_TotalDue_PaymentGoesToOldestFirst()
    {
        var owner = await _client.CreateActiveOwnerAsync();
        var token = owner.Tokens.AccessToken;
        var today = TestData.Today(factory);
        var november = new DateOnly(today.Year, today.Month, 1).AddMonths(-2);
        var december = november.AddMonths(1);
        var propertyId = await _client.CreatePropertyAsync(token, anchorDay: 1, chargeMode: "Postpaid");
        var roomId = await _client.CreateRoomAsync(token, propertyId);
        var renter = await _client.CreateRenterAsync(token);
        var contractId = await (await _client.PostJsonAsync("/api/v1/contracts", new
        {
            roomId, contract = new { representativeRenterId = renter, startDate = november, monthlyRent = Rent, depositAmount = 0, occupants = new[] { new { renterId = renter } } }
        }, token)).ReadIdAsync();
        (await _client.PostJsonAsync($"/api/v1/contracts/{contractId}/activate", null, token)).StatusCode.Should().Be(HttpStatusCode.NoContent);

        async Task<Guid> BillAsync(DateOnly month, bool finalize)
        {
            await _client.PostJsonAsync("/api/v1/invoices/generate", new { propertyId, billingMonth = $"{month:yyyy-MM}" }, token);
            var page = await (await _client.GetAsync($"/api/v1/invoices?contractId={contractId}&billingMonth={month:yyyy-MM}", token)).ReadAsync<JsonElement>();
            var id = page.GetProperty("items")[0].GetProperty("id").GetGuid();
            if (finalize)
                (await _client.PostJsonAsync($"/api/v1/invoices/{id}/finalize", null, token)).StatusCode.Should().Be(HttpStatusCode.OK);
            return id;
        }
        async Task<JsonElement> DetailAsync(Guid id) => await (await _client.GetAsync($"/api/v1/invoices/{id}", token)).ReadAsync<JsonElement>();

        var first = await BillAsync(november, finalize: true);
        var second = await BillAsync(december, finalize: false);
        var draft = await DetailAsync(second);
        draft.GetProperty("previousDebts").EnumerateArray().Select(d => d.GetProperty("invoiceId").GetGuid()).Should().Equal(first);
        draft.GetProperty("totalDue").GetDecimal().Should().Be(2 * Rent, "nháp tháng 12 + nợ tháng 11");
        draft.GetProperty("summary").GetProperty("totalAmount").GetDecimal().Should().Be(Rent, "nợ cũ không cộng vào phiếu");
        (await DetailAsync(first)).GetProperty("previousDebts").GetArrayLength().Should().Be(0);

        await _client.PostJsonAsync($"/api/v1/invoices/{second}/finalize", null, token);
        (await _client.PostJsonAsync($"/api/v1/contracts/{contractId}/payments", new { amount = 4_000_000, method = "BankTransfer", paidAt = today }, token))
            .StatusCode.Should().Be(HttpStatusCode.Created);

        (await DetailAsync(first)).GetProperty("summary").GetProperty("outstanding").GetDecimal().Should().Be(0, "trừ tháng 11 trước");
        var after = await DetailAsync(second);
        after.GetProperty("previousDebts").GetArrayLength().Should().Be(0);
        after.GetProperty("totalDue").GetDecimal().Should().Be(2_000_000);
    }
}
