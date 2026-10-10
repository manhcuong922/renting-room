using System.Net;
using System.Text.Json;
using renting_room.IntegrationTests.Infrastructure;

namespace renting_room.IntegrationTests;

/// <summary>M06 M2 (MT-UC-08, MT-BR-18): thay công tơ hàng loạt giữa tháng, lưu tất cả hoặc không; tính tiền cộng 2 công tơ.</summary>
[Collection(ApiCollection.Name)]
public sealed class MeterBulkReplaceTests(ApiFactory factory)
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task BulkReplace_InvalidRowSavesNothing_ValidBatchReplacesAll_InvoiceSumsOldAndNewMeter()
    {
        var owner = await _client.CreateActiveOwnerAsync();
        var token = owner.Tokens.AccessToken;
        var today = TestData.Today(factory);
        var month = new DateOnly(today.Year, today.Month, 1).AddMonths(-1);
        var propertyId = await _client.CreatePropertyAsync(token, anchorDay: 1, chargeMode: "Postpaid");
        var electricity = await _client.FeeIdAsync(token, propertyId, "Điện");
        await _client.PostJsonAsync($"/api/v1/fee-types/{electricity}/prices", new { effectiveFrom = month.AddDays(-30), unitPrice = 3500 }, token);
        var roomA = await _client.CreateRoomAsync(token, propertyId, "A1");
        var roomB = await _client.CreateRoomAsync(token, propertyId, "A2");
        var meterA = await _client.InstallMeterAsync(token, roomA, electricity, month.AddDays(-1), 100);
        var meterB = await _client.InstallMeterAsync(token, roomB, electricity, month.AddDays(-1), 500);
        var renter = await _client.CreateRenterAsync(token);
        var contractA = await (await _client.PostJsonAsync("/api/v1/contracts", new
        {
            roomId = roomA, contract = new { representativeRenterId = renter, startDate = month, monthlyRent = 3_000_000, depositAmount = 0, occupants = new[] { new { renterId = renter } } }
        }, token)).ReadIdAsync();
        (await _client.PostJsonAsync($"/api/v1/contracts/{contractA}/activate",
            new { handoverReadings = new[] { new { meterId = meterA, value = (decimal?)null } } }, token)).StatusCode.Should().Be(HttpStatusCode.NoContent);

        var sheet = await (await _client.GetAsync($"/api/v1/properties/{propertyId}/meters/replace-sheet?feeTypeId={electricity}", token))
            .ReadAsync<JsonElement>();
        sheet.EnumerateArray().Select(r => r.GetProperty("meterId").GetGuid()).Should().BeEquivalentTo([meterA, meterB]);
        sheet.EnumerateArray().Single(r => r.GetProperty("meterId").GetGuid() == meterB).GetProperty("lastValue").GetDecimal().Should().Be(500);

        var replacedOn = month.AddDays(10);
        Task<HttpResponseMessage> ReplaceAsync(decimal oldB) => _client.PostJsonAsync($"/api/v1/properties/{propertyId}/meters/bulk-replace", new
        {
            feeTypeId = electricity, replacedOn, note = "Điện lực thay đồng loạt",
            rows = new object[]
            {
                new { meterId = meterA, oldFinalValue = 150, newSerialNo = "EVN-A1", newInitialValue = (decimal?)null },
                new { meterId = meterB, oldFinalValue = oldB, newSerialNo = "EVN-A2", newInitialValue = (decimal?)0 }
            }
        }, token);

        var invalid = await ReplaceAsync(450);
        (await invalid.ReadProblemCodeAsync()).Should().Be("BULK_REPLACE_INVALID");
        var rowErrors = (await (await ReplaceAsync(450)).ReadAsync<JsonElement>()).GetProperty("rowErrors");
        rowErrors.GetArrayLength().Should().Be(1);
        rowErrors[0].GetProperty("index").GetInt32().Should().Be(1, "số cuối A2 (450) nhỏ hơn chỉ số cũ 500");
        (await (await _client.GetAsync($"/api/v1/properties/{propertyId}/meters/replace-sheet?feeTypeId={electricity}", token)).ReadAsync<JsonElement>())
            .EnumerateArray().Select(r => r.GetProperty("meterId").GetGuid()).Should().BeEquivalentTo([meterA, meterB], "lỗi 1 dòng ⇒ không lưu dòng nào");

        var replaced = await ReplaceAsync(560);
        replaced.StatusCode.Should().Be(HttpStatusCode.OK, await replaced.Content.ReadAsStringAsync());
        var result = await replaced.ReadAsync<JsonElement>();
        result.GetProperty("replaced").GetInt32().Should().Be(2);
        var newMeterA = (await (await _client.GetAsync($"/api/v1/properties/{propertyId}/meters/replace-sheet?feeTypeId={electricity}", token))
            .ReadAsync<JsonElement>()).EnumerateArray().Single(r => r.GetProperty("roomCode").GetString() == "A1");
        newMeterA.GetProperty("serialNo").GetString().Should().Be("EVN-A1");
        newMeterA.GetProperty("lastValue").GetDecimal().Should().Be(0, "chỉ số đầu công tơ mới mặc định 0");

        // Cuối tháng: công tơ mới 30 ⇒ sản lượng = (150 − 100) + 30 = 80 kWh.
        (await _client.PutJsonAsync($"/api/v1/properties/{propertyId}/meter-readings", new
        {
            readings = new[] { new { meterId = newMeterA.GetProperty("meterId").GetGuid(), contractId = contractA, closingPeriodStart = month,
                readingDate = month.AddMonths(1), value = 30m } }
        }, token)).StatusCode.Should().Be(HttpStatusCode.OK);
        await _client.PostJsonAsync("/api/v1/invoices/generate", new { propertyId, billingMonth = $"{month:yyyy-MM}" }, token);
        var page = await (await _client.GetAsync($"/api/v1/invoices?contractId={contractA}", token)).ReadAsync<JsonElement>();
        var invoice = await (await _client.GetAsync($"/api/v1/invoices/{page.GetProperty("items")[0].GetProperty("id").GetGuid()}", token))
            .ReadAsync<JsonElement>();
        var metered = invoice.GetProperty("lines").EnumerateArray().Single(l => l.GetProperty("type").GetString() == "Metered");
        metered.GetProperty("quantity").GetDecimal().Should().Be(80);
        metered.GetProperty("segments").GetArrayLength().Should().Be(2);
    }
}
