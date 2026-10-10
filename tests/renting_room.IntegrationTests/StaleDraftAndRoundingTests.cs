using System.Net;
using System.Text.Json;
using renting_room.IntegrationTests.Infrastructure;

namespace renting_room.IntegrationTests;

/// <summary>M07 nhóm 2: cờ "Cần tính lại" cho nháp lập sớm (BL-BR-20, I1) và làm tròn tổng phiếu xuống nghìn (BL-BR-29, H1).</summary>
[Collection(ApiCollection.Name)]
public sealed class StaleDraftAndRoundingTests(ApiFactory factory)
{
    private readonly HttpClient _client = factory.CreateClient();

    private sealed record Setup(string Token, Guid PropertyId, Guid ContractId, Guid MeterId, Guid ElectricityId, DateOnly Month);

    /// <summary>Khu thu sau chốt ngày 1, HĐ từ đầu tháng trước, điện 3.500đ, chỉ số nhận phòng 100 — nháp tháng trước lập sớm (chưa có chỉ số cuối).</summary>
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
        await _client.PostJsonAsync("/api/v1/invoices/generate", new { propertyId, billingMonth = $"{month:yyyy-MM}" }, token);
        return new Setup(token, propertyId, contractId, meter, electricity, month);
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
    public async Task EarlyDraft_FlaggedWhenReadingOrPriceOrContractChanges_BulkRecalculateClearsFlag()
    {
        var s = await ArrangeAsync();
        (await DraftAsync(s)).GetProperty("isStale").GetBoolean().Should().BeFalse("vừa tính");

        (await SaveReadingAsync(s, 187)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await DraftAsync(s)).GetProperty("isStale").GetBoolean().Should().BeTrue("nhập chỉ số sau khi lập nháp");
        var stale = await (await _client.GetAsync($"/api/v1/invoices?propertyId={s.PropertyId}&stale=true", s.Token)).ReadAsync<JsonElement>();
        stale.GetProperty("totalCount").GetInt32().Should().Be(1);

        var recalculated = await RecalculateStaleAsync(s);
        recalculated.GetProperty("recalculated").GetInt32().Should().Be(1);
        var draft = await DraftAsync(s);
        draft.GetProperty("isStale").GetBoolean().Should().BeFalse();
        draft.GetProperty("errorCount").GetInt32().Should().Be(0, "đã có chỉ số cuối");
        (await RecalculateStaleAsync(s)).GetProperty("recalculated").GetInt32().Should().Be(0, "không còn nháp cần tính lại");

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
