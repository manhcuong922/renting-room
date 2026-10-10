using System.Net;
using System.Text.Json;
using renting_room.IntegrationTests.Infrastructure;

namespace renting_room.IntegrationTests;

/// <summary>M08 F2 — "Thanh toán + bỏ phần còn lại" và bỏ nợ riêng bất kỳ lúc nào (PM-BR-16/29): chủ trọ, hoặc phó quản lý được cấp quyền riêng.</summary>
[Collection(ApiCollection.Name)]
public sealed class WriteOffTests(ApiFactory factory)
{
    private const decimal Rent = 3_000_000;
    private readonly HttpClient _client = factory.CreateClient();

    /// <summary>Khu thu trước chốt ngày 1, HĐ từ đầu tháng ⇒ phiếu tháng này 3tr đã chốt, chưa thu.</summary>
    private async Task<(string Token, Guid ContractId, Guid InvoiceId)> ArrangeAsync()
    {
        var owner = await _client.CreateActiveOwnerAsync();
        var token = owner.Tokens.AccessToken;
        var today = TestData.Today(factory);
        var start = new DateOnly(today.Year, today.Month, 1);
        var propertyId = await _client.CreatePropertyAsync(token, anchorDay: 1, chargeMode: "Prepaid");
        var roomId = await _client.CreateRoomAsync(token, propertyId);
        var renter = await _client.CreateRenterAsync(token);
        var contractId = await (await _client.PostJsonAsync("/api/v1/contracts", new
        {
            roomId, contract = new { representativeRenterId = renter, startDate = start, monthlyRent = Rent, depositAmount = 0, occupants = new[] { new { renterId = renter } } }
        }, token)).ReadIdAsync();
        (await _client.PostJsonAsync($"/api/v1/contracts/{contractId}/activate", null, token)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        await _client.PostJsonAsync("/api/v1/invoices/generate", new { propertyId, billingMonth = $"{start:yyyy-MM}" }, token);
        var page = await (await _client.GetAsync($"/api/v1/invoices?contractId={contractId}", token)).ReadAsync<JsonElement>();
        var invoiceId = page.GetProperty("items")[0].GetProperty("id").GetGuid();
        (await _client.PostJsonAsync($"/api/v1/invoices/{invoiceId}/finalize", null, token)).StatusCode.Should().Be(HttpStatusCode.OK);
        return (token, contractId, invoiceId);
    }

    private async Task<JsonElement> SummaryAsync(string token, Guid invoiceId) =>
        (await (await _client.GetAsync($"/api/v1/invoices/{invoiceId}", token)).ReadAsync<JsonElement>()).GetProperty("summary");

    private Task<HttpResponseMessage> PayAndWriteOffAsync(string token, Guid invoiceId, decimal amount, string reason = "Khó khăn, chủ trọ cho 1tr") =>
        _client.PostJsonAsync($"/api/v1/invoices/{invoiceId}/pay-and-write-off",
            new { amount, method = "Cash", paidAt = TestData.Today(factory), payerName = (string?)null, reference = (string?)null, reason }, token);

    [Fact]
    public async Task PayTwoMillion_WriteOffRest_InvoiceClosed_OnlyRealMoneyIsReceipt()
    {
        var (token, contractId, invoiceId) = await ArrangeAsync();

        (await (await PayAndWriteOffAsync(token, invoiceId, 4_000_000)).ReadProblemCodeAsync()).Should().Be("PAYMENT_EXCEEDS_DEBT");
        (await (await PayAndWriteOffAsync(token, invoiceId, Rent)).ReadProblemCodeAsync()).Should().Be("NOTHING_TO_WRITE_OFF");
        (await PayAndWriteOffAsync(token, invoiceId, 2_000_000, reason: "")).StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var response = await PayAndWriteOffAsync(token, invoiceId, 2_000_000);
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var result = await response.ReadAsync<JsonElement>();
        result.GetProperty("payment").GetProperty("kind").GetString().Should().Be("Receipt");
        result.GetProperty("payment").GetProperty("amount").GetDecimal().Should().Be(2_000_000);
        result.GetProperty("writeOff").GetProperty("kind").GetString().Should().Be("WriteOff");
        result.GetProperty("writeOff").GetProperty("amount").GetDecimal().Should().Be(1_000_000);

        var summary = await SummaryAsync(token, invoiceId);
        summary.GetProperty("paymentStatus").GetString().Should().Be("WrittenOff");
        summary.GetProperty("paidAmount").GetDecimal().Should().Be(Rent);
        var payments = await (await _client.GetAsync($"/api/v1/payments?contractId={contractId}", token)).ReadAsync<JsonElement>();
        payments.EnumerateArray().Where(p => p.GetProperty("kind").GetString() == "Receipt").Sum(p => p.GetProperty("amount").GetDecimal())
            .Should().Be(2_000_000, "tiền thu thật chỉ 2tr");
    }

    [Fact]
    public async Task WriteOffAlone_WhileActive_ManagerNeedsPermissionGrantedPerPerson()
    {
        var (token, _, invoiceId) = await ArrangeAsync();
        var created = await _client.PostJsonAsync("/api/v1/org/members", new { fullName = "Phó Quản Lý", phone = TestApi.NewPhone() }, token);
        var credentials = await created.ReadAsync<TemporaryCredentialsResponse>();
        var login = await _client.LoginAsync(credentials.Username, credentials.TemporaryPassword);
        var manager = await (await _client.PostJsonAsync("/api/v1/auth/change-password",
            new { currentPassword = credentials.TemporaryPassword, newPassword = "PhoQuanLy2026" }, login.AccessToken)).ReadAsync<TokenResponse>();

        (await _client.PostJsonAsync($"/api/v1/invoices/{invoiceId}/write-off", new { amount = 500_000, reason = "Giảm do sự cố" }, manager.AccessToken))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden, "phó quản lý chưa được cấp quyền bỏ nợ");
        (await PayAndWriteOffAsync(manager.AccessToken, invoiceId, 2_000_000)).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        // Chủ trọ bật quyền cho riêng phó quản lý này ⇒ được bỏ nợ.
        (await _client.PutJsonAsync($"/api/v1/org/members/{credentials.UserId}/write-off-permission", new { allowed = true }, token))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        var writeOff = await _client.PostJsonAsync($"/api/v1/invoices/{invoiceId}/write-off", new { amount = 500_000, reason = "Giảm do sự cố" },
            manager.AccessToken);
        writeOff.StatusCode.Should().Be(HttpStatusCode.OK, await writeOff.Content.ReadAsStringAsync());
        var summary = await SummaryAsync(token, invoiceId);
        summary.GetProperty("paidAmount").GetDecimal().Should().Be(500_000);
        summary.GetProperty("paymentStatus").GetString().Should().Be("PartiallyPaid", "còn 2,5tr vẫn là nợ");

        (await _client.PostJsonAsync($"/api/v1/invoices/{invoiceId}/write-off", new { reason = "Bỏ nốt" }, token)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await SummaryAsync(token, invoiceId)).GetProperty("paymentStatus").GetString().Should().Be("WrittenOff");
    }
}
