using System.Net;
using System.Text.Json;
using Npgsql;
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

        (await _client.PostJsonAsync($"/api/v1/contracts/{contractId}/liquidation/start", new { actualEndDate = today, reason = "MutualAgreement", finalReadings = new[] { new { meterId, value = (decimal?)220 } } }, token))
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

    /// <summary>
    /// H-03 (M06 MT-BR-12, M07 §12 kịch bản 3b): số đầu kỳ lấy từ chỉ số chốt kỳ liền trước trên lưới, không từ phiếu kỳ trước —
    /// hủy chỉ số cuối kỳ k thì nháp k+1 báo thiếu chỉ số chứ không lùi về kỳ k−1 (gộp sản lượng 2 kỳ); nhập lại thì cả hai tự đúng.
    /// </summary>
    [Theory]
    [InlineData("Postpaid")]
    [InlineData("Prepaid")]
    public async Task VoidingClosingReading_NextDraftReportsMissingReading_InsteadOfChainingToOlderReading(string chargeMode)
    {
        var owner = await _client.CreateActiveOwnerAsync();
        var token = owner.Tokens.AccessToken;
        var today = TestData.Today(factory);
        var prepaid = chargeMode == "Prepaid";
        // Thu sau: phiếu tháng M tính điện tháng M. Thu trước: phiếu tháng M tính điện tháng M−1 (phiếu đầu không có điện).
        var first = new DateOnly(today.Year, today.Month, 1).AddMonths(prepaid ? -3 : -2);
        var k = prepaid ? first.AddMonths(1) : first; // phiếu có điện đến chỉ số chốt kỳ `first`
        var propertyId = await _client.CreatePropertyAsync(token, anchorDay: 1, chargeMode: chargeMode);
        var electricity = await _client.FeeIdAsync(token, propertyId, "Điện");
        await _client.PostJsonAsync($"/api/v1/fee-types/{electricity}/prices", new { effectiveFrom = first.AddDays(-30), unitPrice = 3000 }, token);
        var roomId = await _client.CreateRoomAsync(token, propertyId);
        var meterId = await _client.InstallMeterAsync(token, roomId, electricity, first.AddDays(-1), 1000);
        var contractId = await _client.CreateContractAsync(token, roomId, await _client.CreateRenterAsync(token), first);
        (await _client.PostJsonAsync($"/api/v1/contracts/{contractId}/activate",
            new { handoverReadings = new[] { new { meterId, value = (decimal?)null } } }, token)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        Task<HttpResponseMessage> SaveAsync(DateOnly closing, decimal value) => _client.PutJsonAsync($"/api/v1/properties/{propertyId}/meter-readings",
            new { readings = new[] { new { meterId, contractId, closingPeriodStart = closing, readingDate = closing.AddMonths(1), value } } }, token);
        async Task<(decimal? Kwh, List<string?> Issues)> ElectricityAsync(Guid invoiceId)
        {
            var detail = await (await _client.GetAsync($"/api/v1/invoices/{invoiceId}", token)).ReadAsync<JsonElement>();
            var metered = detail.GetProperty("lines").EnumerateArray().Where(l => l.GetProperty("type").GetString() == "Metered").ToList();
            return (metered.Count == 0 ? null : metered.Sum(l => l.GetProperty("quantity").GetDecimal()),
                detail.GetProperty("issues").EnumerateArray().Select(i => i.GetProperty("code").GetString()).ToList());
        }

        (await SaveAsync(first, 1080)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await SaveAsync(first.AddMonths(1), 1160)).StatusCode.Should().Be(HttpStatusCode.OK);
        if (prepaid)
            (await GenerateAsync(propertyId, first, token)).GetProperty("created").GetInt32().Should().Be(1);
        (await GenerateAsync(propertyId, k, token)).GetProperty("created").GetInt32().Should().Be(1);
        (await GenerateAsync(propertyId, k.AddMonths(1), token)).GetProperty("created").GetInt32().Should().Be(1);
        var draftK = await InvoiceIdAsync(contractId, k, token);
        var draftNext = await InvoiceIdAsync(contractId, k.AddMonths(1), token);
        (await ElectricityAsync(draftK)).Kwh.Should().Be(80);
        (await ElectricityAsync(draftNext)).Kwh.Should().Be(80);

        // Hủy chỉ số chốt kỳ `first` (1.080): cả 2 nháp thiếu chỉ số — kỳ sau KHÔNG lùi về 1.000 (160 kWh).
        var closingK = (await (await _client.GetAsync($"/api/v1/meters/{meterId}/readings", token)).ReadAsync<List<JsonElement>>())
            .Single(r => r.GetProperty("kind").GetString() == "Periodic" && r.GetProperty("value").GetDecimal() == 1080);
        (await _client.PostJsonAsync($"/api/v1/meter-readings/{closingK.GetProperty("id").GetGuid()}/void", new { reason = "Ghi nhầm" }, token))
            .IsSuccessStatusCode.Should().BeTrue();
        (await ElectricityAsync(draftK)).Issues.Should().Contain("MISSING_READING");
        var next = await ElectricityAsync(draftNext);
        next.Issues.Should().Contain("MISSING_READING");
        next.Kwh.Should().BeNull("không được gộp 1.000 → 1.160");
        (await _client.PostJsonAsync("/api/v1/invoices/recalculate", new { invoiceIds = new[] { draftNext } }, token)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await ElectricityAsync(draftNext)).Kwh.Should().BeNull();
        var refused = await _client.PostJsonAsync($"/api/v1/invoices/{draftNext}/finalize", null, token);
        refused.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity, await refused.Content.ReadAsStringAsync());

        // Nhập lại 1.090: cả hai tự tính lại, không đụng ràng buộc điểm đầu (409).
        var saved = await SaveAsync(first, 1090);
        saved.StatusCode.Should().Be(HttpStatusCode.OK, await saved.Content.ReadAsStringAsync());
        (await ElectricityAsync(draftK)).Should().Match<(decimal? Kwh, List<string?> Issues)>(x => x.Kwh == 90 && !x.Issues.Contains("MISSING_READING"));
        (await ElectricityAsync(draftNext)).Should().Match<(decimal? Kwh, List<string?> Issues)>(x => x.Kwh == 70 && !x.Issues.Contains("MISSING_READING"));
    }

    /// <summary>
    /// M-21 (M07 BL-BR-22, §10, §12 kịch bản 3c): xóa nháp khóa HĐ trước phiếu như tạo phiếu / Void / chốt — không thì xóa nháp kỳ k
    /// song song tạo kỳ k+1 lọt kiểm tra "mới nhất" (k+1 chưa commit) ⇒ còn k+1, mất k, không khoảng ngắt nào ghi lại.
    /// </summary>
    [Fact]
    public async Task DeleteDraft_WaitsForContractLock_HeldByConcurrentGeneration()
    {
        var owner = await _client.CreateActiveOwnerAsync();
        var token = owner.Tokens.AccessToken;
        var today = TestData.Today(factory);
        var month = new DateOnly(today.Year, today.Month, 1).AddMonths(-1);
        var propertyId = await _client.CreatePropertyAsync(token, anchorDay: 1, chargeMode: "Prepaid");
        var roomId = await _client.CreateRoomAsync(token, propertyId);
        var contractId = await _client.CreateContractAsync(token, roomId, await _client.CreateRenterAsync(token), month);
        (await _client.PostJsonAsync($"/api/v1/contracts/{contractId}/activate", null, token)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await GenerateAsync(propertyId, month, token)).GetProperty("created").GetInt32().Should().Be(1);
        var draft = await InvoiceIdAsync(contractId, month, token);

        // Giả lập lệnh tạo phiếu kỳ sau đang chạy: giữ khóa hàng contracts của HĐ.
        await using var connection = new NpgsqlConnection(factory.ConnectionString);
        await connection.OpenAsync();
        await using var holder = await connection.BeginTransactionAsync();
        await using (var lockContract = new NpgsqlCommand("SELECT 1 FROM contracts WHERE id = @id FOR UPDATE", connection, holder))
        {
            lockContract.Parameters.AddWithValue("id", contractId);
            await lockContract.ExecuteScalarAsync();
        }

        var delete = factory.CreateClient().DeleteAsync($"/api/v1/invoices/{draft}", token);
        (await Task.WhenAny(delete, Task.Delay(TimeSpan.FromSeconds(1)))).Should().NotBeSameAs(delete, "xóa nháp phải chờ khóa HĐ");

        await holder.RollbackAsync();
        (await delete).StatusCode.Should().Be(HttpStatusCode.NoContent);
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
