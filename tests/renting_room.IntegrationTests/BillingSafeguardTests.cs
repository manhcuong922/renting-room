using System.Net;
using System.Text.Json;
using renting_room.IntegrationTests.Infrastructure;

namespace renting_room.IntegrationTests;

/// <summary>
/// M07 bổ sung 17/10/2026: phiếu quyết toán là phiếu mới nhất (BL-BR-22), nháp chỉ lập khi đủ chỉ số / kỳ trước không còn nháp thiếu chỉ số
/// (BL-BR-21), hoàn trả không vượt số đã thu khi sửa dòng và khi chốt (BL-BR-27).
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class BillingSafeguardTests(ApiFactory factory)
{
    private readonly HttpClient _client = factory.CreateClient();

    private async Task<JsonElement> GenerateAsync(Guid propertyId, DateOnly month, string token) =>
        await (await _client.PostJsonAsync("/api/v1/invoices/generate", new { propertyId, billingMonth = $"{month:yyyy-MM}" }, token)).ReadAsync<JsonElement>();

    private async Task<Guid> InvoiceIdAsync(Guid contractId, DateOnly month, string token)
    {
        var page = await (await _client.GetAsync($"/api/v1/invoices?contractId={contractId}&billingMonth={month:yyyy-MM}", token)).ReadAsync<JsonElement>();
        return page.GetProperty("items").EnumerateArray().First(i => i.GetProperty("status").GetString() != "Void").GetProperty("id").GetGuid();
    }

    private async Task<Guid> FinalizedInvoiceAsync(Guid propertyId, Guid contractId, DateOnly month, string token)
    {
        (await GenerateAsync(propertyId, month, token)).GetProperty("created").GetInt32().Should().Be(1);
        var id = await InvoiceIdAsync(contractId, month, token);
        var finalized = await _client.PostJsonAsync($"/api/v1/invoices/{id}/finalize", null, token);
        finalized.StatusCode.Should().Be(HttpStatusCode.OK, await finalized.Content.ReadAsStringAsync());
        return id;
    }

    [Fact]
    public async Task FinalInvoiceIsLatest_LastRegularInvoiceCannotBeVoidedWhileItExists()
    {
        var owner = await _client.CreateActiveOwnerAsync();
        var token = owner.Tokens.AccessToken;
        var today = TestData.Today(factory);
        var month = new DateOnly(today.Year, today.Month, 1);
        var propertyId = await _client.CreatePropertyAsync(token, anchorDay: 1, chargeMode: "Prepaid");
        var roomId = await _client.CreateRoomAsync(token, propertyId);
        var contractId = await _client.CreateContractAsync(token, roomId, await _client.CreateRenterAsync(token), month.AddMonths(-1));
        (await _client.PostJsonAsync($"/api/v1/contracts/{contractId}/activate", null, token)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        await FinalizedInvoiceAsync(propertyId, contractId, month.AddMonths(-1), token);
        var lastRegular = await FinalizedInvoiceAsync(propertyId, contractId, month, token); // thu trước tiền phòng cả tháng này

        (await _client.PostJsonAsync($"/api/v1/contracts/{contractId}/liquidation/start", new { actualEndDate = today, reason = "MutualAgreement" }, token))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);
        var final = await (await _client.PostJsonAsync($"/api/v1/contracts/{contractId}/final-invoice", new { }, token)).ReadAsync<JsonElement>();
        final.GetProperty("lines").EnumerateArray().Should().NotContain(l => l.GetProperty("type").GetString() == "Rent", "tiền phòng kỳ cuối đã nằm trên phiếu tháng này");

        var refused = await _client.PostJsonAsync($"/api/v1/invoices/{lastRegular}/void", new { reason = "Sai" }, token);
        (await refused.ReadProblemCodeAsync()).Should().Be("NOT_LATEST_INVOICE", "hủy phiếu này sẽ mất tiền phòng kỳ cuối");

        (await _client.DeleteAsync($"/api/v1/invoices/{final.GetProperty("summary").GetProperty("id").GetGuid()}", token)).StatusCode
            .Should().Be(HttpStatusCode.NoContent);
        (await _client.PostJsonAsync($"/api/v1/invoices/{lastRegular}/void", new { reason = "Sai" }, token)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    /// <summary>
    /// BL-BR-05, 17: phiếu quyết toán khu thu trước, kỳ trước chưa thu ⇒ 2 dòng Điện; tính lại và chốt được (trước 18/10/2026 trùng khóa dòng),
    /// mỗi dòng chỉ có đoạn đo của kỳ mình.
    /// </summary>
    [Fact]
    public async Task FinalInvoice_TwoMeteredLines_RecalculateAndFinalize_SegmentsPerLine()
    {
        var owner = await _client.CreateActiveOwnerAsync();
        var token = owner.Tokens.AccessToken;
        var today = TestData.Today(factory);
        var month = new DateOnly(today.Year, today.Month, 1);
        var previous = month.AddMonths(-1);
        var propertyId = await _client.CreatePropertyAsync(token, anchorDay: 1, chargeMode: "Prepaid");
        var electricity = await _client.FeeIdAsync(token, propertyId, "Điện");
        await _client.PostJsonAsync($"/api/v1/fee-types/{electricity}/prices", new { effectiveFrom = previous.AddDays(-30), unitPrice = 3500 }, token);
        var roomId = await _client.CreateRoomAsync(token, propertyId, "A1");
        var meterId = await _client.InstallMeterAsync(token, roomId, electricity, previous.AddDays(-1), 100);
        var contractId = await _client.CreateContractAsync(token, roomId, await _client.CreateRenterAsync(token), previous);
        (await _client.PostJsonAsync($"/api/v1/contracts/{contractId}/activate",
            new { handoverReadings = new[] { new { meterId, value = (decimal?)null } } }, token)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        await FinalizedInvoiceAsync(propertyId, contractId, previous, token); // thu trước kỳ đầu: chưa có điện
        (await _client.PutJsonAsync($"/api/v1/properties/{propertyId}/meter-readings",
            new { readings = new[] { new { meterId, contractId, closingPeriodStart = previous, readingDate = month, value = 180m } } }, token))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        (await _client.PostJsonAsync($"/api/v1/contracts/{contractId}/liquidation/start", new { actualEndDate = today, reason = "MutualAgreement" }, token))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);
        var created = await _client.PostJsonAsync($"/api/v1/contracts/{contractId}/final-invoice",
            new { finalReadings = new[] { new { meterId, value = (decimal?)220 } } }, token);
        created.StatusCode.Should().Be(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        var finalId = (await created.ReadAsync<JsonElement>()).GetProperty("summary").GetProperty("id").GetGuid();

        (await _client.PostJsonAsync("/api/v1/invoices/recalculate", new { invoiceIds = new[] { finalId } }, token)).StatusCode.Should().Be(HttpStatusCode.OK);
        var finalized = await _client.PostJsonAsync($"/api/v1/invoices/{finalId}/finalize", null, token);
        finalized.StatusCode.Should().Be(HttpStatusCode.OK, await finalized.Content.ReadAsStringAsync());
        var metered = (await finalized.ReadAsync<JsonElement>()).GetProperty("lines").EnumerateArray()
            .Where(l => l.GetProperty("type").GetString() == "Metered").ToList();
        metered.Select(l => (l.GetProperty("quantity").GetDecimal(), l.GetProperty("segments").GetArrayLength())).Should().Equal((80m, 1), (40m, 1));
        metered[0].GetProperty("segments")[0].GetProperty("endValue").GetDecimal().Should().Be(180);
        metered[1].GetProperty("segments")[0].GetProperty("startValue").GetDecimal().Should().Be(180);
    }

    [Fact]
    public async Task Generate_PreviousDraftLostItsReading_NextPeriodSkippedAsIncomplete()
    {
        var owner = await _client.CreateActiveOwnerAsync();
        var token = owner.Tokens.AccessToken;
        var today = TestData.Today(factory);
        var first = new DateOnly(today.Year, today.Month, 1).AddMonths(-2);
        var propertyId = await _client.CreatePropertyAsync(token, anchorDay: 1, chargeMode: "Postpaid");
        var electricity = await _client.FeeIdAsync(token, propertyId, "Điện");
        await _client.PostJsonAsync($"/api/v1/fee-types/{electricity}/prices", new { effectiveFrom = first.AddDays(-30), unitPrice = 3500 }, token);
        var roomId = await _client.CreateRoomAsync(token, propertyId);
        var meterId = await _client.InstallMeterAsync(token, roomId, electricity, first.AddDays(-1), 100);
        var contractId = await _client.CreateContractAsync(token, roomId, await _client.CreateRenterAsync(token), first);
        (await _client.PostJsonAsync($"/api/v1/contracts/{contractId}/activate",
            new { handoverReadings = new[] { new { meterId, value = (decimal?)null } } }, token)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        Task<HttpResponseMessage> SaveAsync(DateOnly closing, decimal value) => _client.PutJsonAsync($"/api/v1/properties/{propertyId}/meter-readings",
            new { readings = new[] { new { meterId, contractId, closingPeriodStart = closing, readingDate = closing.AddMonths(1), value } } }, token);

        (await SaveAsync(first, 150)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await GenerateAsync(propertyId, first, token)).GetProperty("created").GetInt32().Should().Be(1);
        var draftId = await InvoiceIdAsync(contractId, first, token);

        // Hủy chỉ số cuối kỳ sau khi đã lập nháp ⇒ tính lại, nháp mang lỗi MISSING_READING.
        var periodic = (await (await _client.GetAsync($"/api/v1/meters/{meterId}/readings", token)).ReadAsync<List<JsonElement>>())
            .Single(r => r.GetProperty("kind").GetString() == "Periodic");
        (await _client.PostJsonAsync($"/api/v1/meter-readings/{periodic.GetProperty("id").GetGuid()}/void", new { reason = "Ghi nhầm" }, token))
            .IsSuccessStatusCode.Should().BeTrue();
        await _client.PostJsonAsync("/api/v1/invoices/recalculate", new { invoiceIds = new[] { draftId } }, token);

        (await SaveAsync(first.AddMonths(1), 200)).StatusCode.Should().Be(HttpStatusCode.OK);
        var next = await GenerateAsync(propertyId, first.AddMonths(1), token);
        next.GetProperty("created").GetInt32().Should().Be(0);
        next.GetProperty("skipped").EnumerateArray().Select(x => x.GetProperty("reason").GetString()).Should().Equal("PREVIOUS_PERIOD_INCOMPLETE");
    }

    [Fact]
    public async Task Refund_EditAboveCollected_AndFinalizeAfterSourcePaymentReversed_AreRefused()
    {
        var owner = await _client.CreateActiveOwnerAsync();
        var token = owner.Tokens.AccessToken;
        var today = TestData.Today(factory);
        var month = new DateOnly(today.Year, today.Month, 1);
        var propertyId = await _client.CreatePropertyAsync(token, anchorDay: 1, chargeMode: "Postpaid");
        var roomId = await _client.CreateRoomAsync(token, propertyId);
        var contractId = await _client.CreateContractAsync(token, roomId, await _client.CreateRenterAsync(token), month.AddMonths(-1));
        (await _client.PostJsonAsync($"/api/v1/contracts/{contractId}/activate", null, token)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        var source = await FinalizedInvoiceAsync(propertyId, contractId, month.AddMonths(-1), token);
        var payment = await (await _client.PostJsonAsync($"/api/v1/contracts/{contractId}/payments",
            new { amount = 1_000_000, method = "Cash", paidAt = today, invoiceId = source }, token)).ReadAsync<JsonElement>();

        (await GenerateAsync(propertyId, month, token)).GetProperty("created").GetInt32().Should().Be(1);
        var draftId = await InvoiceIdAsync(contractId, month, token);
        var added = await (await _client.PostJsonAsync($"/api/v1/invoices/{draftId}/manual-lines", new
        {
            type = "Refund", description = "Hoàn tiền thu nhầm", amount = 100_000, note = "Thu nhầm phí rác", sourceInvoiceId = source
        }, token)).ReadAsync<JsonElement>();
        var refundLine = added.GetProperty("lines").EnumerateArray().Single(l => l.GetProperty("type").GetString() == "Refund").GetProperty("id").GetGuid();

        var edited = await _client.PutJsonAsync($"/api/v1/invoices/{draftId}/lines/{refundLine}", new { amount = 2_000_000, note = "Sửa số" }, token);
        (await edited.ReadProblemCodeAsync()).Should().Be("REFUND_EXCEEDS_PAID", "phiếu nguồn mới thu 1.000.000");

        (await _client.PostJsonAsync($"/api/v1/payments/{payment.GetProperty("id").GetGuid()}/reverse", new { reason = "Nhập nhầm" }, token))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        var finalize = await _client.PostJsonAsync($"/api/v1/invoices/{draftId}/finalize", null, token);
        (await finalize.ReadProblemCodeAsync()).Should().Be("REFUND_EXCEEDS_PAID", "phiếu nguồn đã bị đảo thu — không còn gì để hoàn");
    }
}
