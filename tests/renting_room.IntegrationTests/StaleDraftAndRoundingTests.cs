using System.Net;
using System.Text.Json;
using Npgsql;
using renting_room.IntegrationTests.Infrastructure;

namespace renting_room.IntegrationTests;

/// <summary>M07 nhóm 2: cờ "Cần tính lại" cho nháp lập sớm (BL-BR-20, I1) và làm tròn tổng phiếu xuống nghìn (BL-BR-29, H1).</summary>
[Collection(ApiCollection.Name)]
public sealed class StaleDraftAndRoundingTests(ApiFactory factory)
{
    private readonly HttpClient _client = factory.CreateClient();

    private sealed record Setup(string Token, Guid PropertyId, Guid ContractId, Guid MeterId, Guid ElectricityId, DateOnly Month);

    /// <summary>
    /// Khu thu sau chốt ngày 1, HĐ từ đầu tháng trước, điện 3.500đ, chỉ số nhận phòng 100 — nháp tháng trước lập với chỉ số cuối 150
    /// (BL-BR-21: nháp chỉ lập khi đủ chỉ số), test sửa chỉ số sau đó.
    /// </summary>
    private async Task<Setup> ArrangeAsync()
    {
        var owner = await _client.CreateActiveOwnerAsync();
        var token = owner.Tokens.AccessToken;
        var today = TestData.Today(factory);
        var month = new DateOnly(today.Year, today.Month, 1).AddMonths(-1);
        var propertyId = await _client.CreatePropertyAsync(token, anchorDay: 1, chargeMode: "Postpaid");
        var roomId = await _client.CreateRoomAsync(token, propertyId);
        var electricity = await _client.FeeIdAsync(token, propertyId, "Điện");
        await _client.PostJsonAsync($"/api/v1/fee-types/{electricity}/prices", new { effectiveFrom = month.AddDays(-30), unitPrice = 3500 }, token);
        var meter = await _client.InstallMeterAsync(token, roomId, electricity, month.AddDays(-1), 100);
        var renter = await _client.CreateRenterAsync(token);
        var contractId = await (await _client.PostJsonAsync("/api/v1/contracts", new
        {
            roomId, contract = new { representativeRenterId = renter, startDate = month, monthlyRent = 3_000_000, depositAmount = 0, occupants = new[] { new { renterId = renter } } }
        }, token)).ReadIdAsync();
        (await _client.PostJsonAsync($"/api/v1/contracts/{contractId}/activate",
            new { handoverReadings = new[] { new { meterId = meter, value = (decimal?)null } } }, token)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        var setup = new Setup(token, propertyId, contractId, meter, electricity, month);
        (await SaveReadingAsync(setup, 150)).StatusCode.Should().Be(HttpStatusCode.OK);
        var generated = await (await _client.PostJsonAsync("/api/v1/invoices/generate", new { propertyId, billingMonth = $"{month:yyyy-MM}" }, token))
            .ReadAsync<JsonElement>();
        generated.GetProperty("created").GetInt32().Should().Be(1);
        return setup;
    }

    private async Task<JsonElement> DraftAsync(Setup s)
    {
        var page = await (await _client.GetAsync($"/api/v1/invoices?contractId={s.ContractId}", s.Token)).ReadAsync<JsonElement>();
        return page.GetProperty("items")[0];
    }

    private Task<HttpResponseMessage> SaveReadingAsync(Setup s, decimal value) =>
        _client.PutJsonAsync($"/api/v1/properties/{s.PropertyId}/meter-readings", new
        {
            readings = new[] { new { meterId = s.MeterId, contractId = s.ContractId, closingPeriodStart = s.Month, readingDate = s.Month.AddMonths(1), value } }
        }, s.Token);

    private async Task<JsonElement> RecalculateStaleAsync(Setup s) =>
        await (await _client.PostJsonAsync("/api/v1/invoices/recalculate", new { propertyId = s.PropertyId, staleOnly = true }, s.Token))
            .ReadAsync<JsonElement>();

    [Fact]
    public async Task EarlyDraft_ReadingChangeRecalculatesAtOnce_PriceOrContractChangeFlags_BulkRecalculateClearsFlag()
    {
        var s = await ArrangeAsync();
        (await DraftAsync(s)).GetProperty("isStale").GetBoolean().Should().BeFalse("vừa tính");

        // M06 MT-BR-06 (20/10/2026): sửa chỉ số ⇒ nháp tự tính lại ngay, không qua cờ.
        (await SaveReadingAsync(s, 187)).StatusCode.Should().Be(HttpStatusCode.OK);
        var draft = await DraftAsync(s);
        draft.GetProperty("isStale").GetBoolean().Should().BeFalse("nháp đã tính lại cùng lệnh lưu chỉ số");
        draft.GetProperty("totalAmount").GetDecimal().Should().Be(3_304_000, "87 kWh × 3.500 = 304.500, làm tròn nghìn");
        draft.GetProperty("errorCount").GetInt32().Should().Be(0);
        (await RecalculateStaleAsync(s)).GetProperty("recalculated").GetInt32().Should().Be(0, "không có nháp cần tính lại");

        await _client.PostJsonAsync($"/api/v1/fee-types/{s.ElectricityId}/prices", new { effectiveFrom = s.Month, unitPrice = 3800 }, s.Token);
        (await DraftAsync(s)).GetProperty("isStale").GetBoolean().Should().BeTrue("giá điện có hiệu lực trong kỳ đổi");
        await RecalculateStaleAsync(s);

        (await _client.PutJsonAsync($"/api/v1/contracts/{s.ContractId}/signed-document", new { hasSignedDocument = true, note = "Tủ hồ sơ" }, s.Token))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await DraftAsync(s)).GetProperty("isStale").GetBoolean().Should().BeFalse("bản ký không ảnh hưởng tiền");
        var renter = await _client.CreateRenterAsync(s.Token);
        (await _client.PostJsonAsync($"/api/v1/contracts/{s.ContractId}/occupants",
            new { renterId = renter, moveInDate = s.Month.AddDays(10), relationshipType = "CoTenant" }, s.Token)).IsSuccessStatusCode.Should().BeTrue();
        (await DraftAsync(s)).GetProperty("isStale").GetBoolean().Should().BeTrue("người ở thay đổi");
    }

    private async Task<JsonElement> SheetRowAsync(Setup s) =>
        (await (await _client.GetAsync($"/api/v1/properties/{s.PropertyId}/meter-reading-sheet?billingMonth={s.Month:yyyy-MM}", s.Token))
            .ReadAsync<JsonElement>()).GetProperty("rows")[0];

    /// <summary>L-02 (BL-BR-20, M07 §12 kịch bản 3d): chốt thành công ⇒ hết "Cần tính lại" — kể cả khi nguồn đổi mà không làm lệch tiền.</summary>
    [Fact]
    public async Task Finalize_StaleDraftWhoseAmountsStillMatch_ClearsStaleFlag()
    {
        var s = await ArrangeAsync();
        var laundry = await (await _client.PostJsonAsync($"/api/v1/properties/{s.PropertyId}/fee-types", new
        {
            name = "Giặt sấy", group = "Service", chargeBasis = "PerRoom", unit = "phòng", autoAttach = false,
            initialPrice = new { effectiveFrom = s.Month.AddDays(-30), unitPrice = 50000 }
        }, s.Token)).ReadIdAsync();
        (await _client.PostJsonAsync($"/api/v1/fee-types/{laundry}/prices", new { effectiveFrom = s.Month, unitPrice = 60000 }, s.Token))
            .IsSuccessStatusCode.Should().BeTrue();
        var draft = await DraftAsync(s);
        draft.GetProperty("isStale").GetBoolean().Should().BeTrue("bản giá mới của khu có hiệu lực trong kỳ");
        var id = draft.GetProperty("id").GetGuid();

        var finalized = await _client.PostJsonAsync($"/api/v1/invoices/{id}/finalize", null, s.Token);
        finalized.StatusCode.Should().Be(HttpStatusCode.OK, "phòng không dùng Giặt sấy ⇒ phần hệ thống không đổi");
        (await finalized.ReadAsync<JsonElement>()).GetProperty("summary").GetProperty("isStale").GetBoolean().Should().BeFalse();
        (await DraftAsync(s)).GetProperty("isStale").GetBoolean().Should().BeFalse();
        var row = (await SheetRowAsync(s)).GetProperty("invoice");
        row.GetProperty("status").GetString().Should().Be("Finalized");
        row.GetProperty("isStale").GetBoolean().Should().BeFalse("lưới không được hiện phiếu đã chốt ở bộ lọc \"Cần tính lại\"");
    }

    /// <summary>Kết nối khác giữ khóa HĐ và xóa nháp (chưa commit) — như lệnh xóa nháp đang chạy (M07 §10).</summary>
    private async Task<(NpgsqlConnection Connection, NpgsqlTransaction Holder)> DeleteDraftHoldingContractLockAsync(Setup s, Guid invoiceId)
    {
        var connection = new NpgsqlConnection(factory.ConnectionString);
        await connection.OpenAsync();
        var holder = await connection.BeginTransactionAsync();
        await using var command = new NpgsqlCommand(
            "SELECT 1 FROM contracts WHERE id = @contract FOR UPDATE; DELETE FROM invoices WHERE id = @invoice AND status = 'Draft'", connection, holder);
        command.Parameters.AddWithValue("contract", s.ContractId);
        command.Parameters.AddWithValue("invoice", invoiceId);
        await command.ExecuteNonQueryAsync();
        return (connection, holder);
    }

    private static async Task<HttpResponseMessage> AfterDeleteCommitsAsync(
        (NpgsqlConnection Connection, NpgsqlTransaction Holder) deletion, Task<HttpResponseMessage> request)
    {
        await using var connection = deletion.Connection;
        await using var holder = deletion.Holder;
        (await Task.WhenAny(request, Task.Delay(TimeSpan.FromSeconds(1)))).Should().NotBeSameAs(request, "lệnh phải chờ khóa HĐ");
        await holder.CommitAsync();
        return await request;
    }

    /// <summary>L-03 (M07 §10, §12 kịch bản 3e): nháp bị xóa trong lúc lệnh chờ khóa ⇒ 404, không NullReference 500.</summary>
    [Theory]
    [InlineData("finalize")]
    [InlineData("void")]
    public async Task FinalizeOrVoid_DraftDeletedWhileWaitingForLock_ReturnsNotFound(string action)
    {
        var s = await ArrangeAsync();
        var id = (await DraftAsync(s)).GetProperty("id").GetGuid();

        var deletion = await DeleteDraftHoldingContractLockAsync(s, id);
        var response = await AfterDeleteCommitsAsync(deletion,
            factory.CreateClient().PostJsonAsync($"/api/v1/invoices/{id}/{action}", action == "void" ? new { reason = "Sai" } : null, s.Token));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound, await response.Content.ReadAsStringAsync());
        (await response.ReadProblemCodeAsync()).Should().Be("INVOICE_NOT_FOUND");
    }

    /// <summary>L-03: lưu lưới chỉ số tính lại nháp dùng công tơ — nháp bị xóa giữa chừng thì bỏ qua, chỉ số vẫn lưu (trước: 500, mất cả lệnh).</summary>
    [Fact]
    public async Task SaveReadings_DraftDeletedWhileWaitingForLock_SavesReadingAndSkipsDraft()
    {
        var s = await ArrangeAsync();
        var id = (await DraftAsync(s)).GetProperty("id").GetGuid();

        var deletion = await DeleteDraftHoldingContractLockAsync(s, id);
        var response = await AfterDeleteCommitsAsync(deletion, factory.CreateClient().PutJsonAsync($"/api/v1/properties/{s.PropertyId}/meter-readings", new
        {
            readings = new[] { new { meterId = s.MeterId, contractId = s.ContractId, closingPeriodStart = s.Month, readingDate = s.Month.AddMonths(1), value = 187m } }
        }, s.Token));

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var row = await SheetRowAsync(s);
        row.GetProperty("current").GetProperty("value").GetDecimal().Should().Be(187);
        row.GetProperty("invoice").ValueKind.Should().Be(JsonValueKind.Null, "nháp đã bị xóa");
    }

    [Fact]
    public async Task Total_RoundedDownToThousand_ByDefault_TogglePerProperty()
    {
        var s = await ArrangeAsync();
        await SaveReadingAsync(s, 187); // 87 kWh × 3.500 = 304.500
        await RecalculateStaleAsync(s);
        var draft = await DraftAsync(s);
        draft.GetProperty("totalAmount").GetDecimal().Should().Be(3_304_000);
        draft.GetProperty("roundingAmount").GetDecimal().Should().Be(-500, "dòng \"Làm tròn −500\"");

        var billing = await (await _client.GetAsync($"/api/v1/properties/{s.PropertyId}", s.Token)).ReadAsync<JsonElement>();
        billing.GetProperty("billing").GetProperty("roundInvoiceTotal").GetBoolean().Should().BeTrue("mặc định bật");
        (await _client.PutJsonAsync($"/api/v1/properties/{s.PropertyId}/billing",
            new { anchorDay = 1, chargeMode = "Postpaid", paymentDueDays = 5, prorationMode = "Daily", noticeDays = 30, roundInvoiceTotal = false },
            s.Token)).StatusCode.Should().Be(HttpStatusCode.OK);
        await _client.PostJsonAsync("/api/v1/invoices/recalculate", new { propertyId = s.PropertyId, billingMonth = $"{s.Month:yyyy-MM}" }, s.Token);
        draft = await DraftAsync(s);
        draft.GetProperty("totalAmount").GetDecimal().Should().Be(3_304_500);
        draft.GetProperty("roundingAmount").GetDecimal().Should().Be(0);
    }
}
