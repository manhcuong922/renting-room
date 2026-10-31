using System.Net;
using System.Text.Json;
using renting_room.IntegrationTests.Infrastructure;

namespace renting_room.IntegrationTests;

/// <summary>
/// 28/10/2026 — L-10 (M06 §8): lưu lưới chỉ số sửa cả ngày ghi của chỉ số cuối kỳ đã có; HĐ đã hoàn tất thanh lý không ghi được chỉ số.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class MeterReadingSheetGuardTests(ApiFactory factory)
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task SaveSheet_ResendingARow_UpdatesValueAndReadingDate()
    {
        var owner = await _client.CreateActiveOwnerAsync();
        var token = owner.Tokens.AccessToken;
        var today = TestData.Today(factory);
        var month = new DateOnly(today.Year, today.Month, 1).AddMonths(-1);
        var monthEnd = month.AddMonths(1).AddDays(-1);
        var propertyId = await _client.CreatePropertyAsync(token, anchorDay: 1, chargeMode: "Postpaid");
        var electricity = await _client.FeeIdAsync(token, propertyId, "Điện");
        var roomId = await _client.CreateRoomAsync(token, propertyId, "L10");
        var meterId = await _client.InstallMeterAsync(token, roomId, electricity, month.AddDays(-10), 100);
        var renter = await _client.CreateRenterAsync(token);
        var contractId = await _client.CreateContractAsync(token, roomId, renter, month);
        (await _client.PostJsonAsync($"/api/v1/contracts/{contractId}/activate",
            new { handoverReadings = new[] { new { meterId, value = (decimal?)null } } }, token)).StatusCode.Should().Be(HttpStatusCode.NoContent);

        Task<HttpResponseMessage> SaveAsync(DateOnly readingDate, decimal value) =>
            _client.PutJsonAsync($"/api/v1/properties/{propertyId}/meter-readings",
                new { readings = new[] { new { meterId, contractId, closingPeriodStart = month, readingDate, value } } }, token);
        async Task<List<JsonElement>> PeriodicAsync() =>
            (await (await _client.GetAsync($"/api/v1/meters/{meterId}/readings", token)).ReadAsync<List<JsonElement>>())
            .Where(r => r.GetProperty("kind").GetString() == "Periodic").ToList();

        (await SaveAsync(month.AddDays(24), 200)).StatusCode.Should().Be(HttpStatusCode.OK);
        var resent = await SaveAsync(monthEnd, 210);
        resent.StatusCode.Should().Be(HttpStatusCode.OK, await resent.Content.ReadAsStringAsync());
        var periodic = (await PeriodicAsync()).Should().ContainSingle().Subject;
        periodic.GetProperty("value").GetDecimal().Should().Be(210);
        periodic.GetProperty("readingDate").GetString().Should().Be($"{monthEnd:yyyy-MM-dd}");

        // Chỉ đổi ngày, giữ số.
        (await SaveAsync(month.AddDays(27), 210)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await PeriodicAsync()).Single().GetProperty("readingDate").GetString().Should().Be($"{month.AddDays(27):yyyy-MM-dd}");

        // Ngày mới trước ngày lắp công tơ ⇒ lỗi dòng, giữ số và ngày cũ.
        var invalid = await SaveAsync(month.AddDays(-11), 210);
        invalid.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        var problem = await invalid.ReadAsync<JsonElement>();
        problem.GetProperty("code").GetString().Should().Be("READINGS_INVALID");
        problem.GetProperty("rowErrors").EnumerateArray().Single().GetProperty("code").GetString().Should().Be("INVALID_READING_DATE");
        (await PeriodicAsync()).Single().GetProperty("readingDate").GetString().Should().Be($"{month.AddDays(27):yyyy-MM-dd}");
    }

    [Fact]
    public async Task SaveSheet_ForEndedContract_IsRejected()
    {
        var owner = await _client.CreateActiveOwnerAsync();
        var token = owner.Tokens.AccessToken;
        var today = TestData.Today(factory);
        var start = TestData.StartWithinOnePeriod(today, 10);
        var propertyId = await _client.CreatePropertyAsync(token, anchorDay: start.Day);
        var electricity = await _client.FeeIdAsync(token, propertyId, "Điện");
        await _client.PostJsonAsync($"/api/v1/fee-types/{electricity}/prices", new { effectiveFrom = start.AddDays(-30), unitPrice = 3800 }, token);
        var roomId = await _client.CreateRoomAsync(token, propertyId);
        var meterId = await _client.InstallMeterAsync(token, roomId, electricity, start.AddDays(-5), 100);
        var renter = await _client.CreateRenterAsync(token);
        var contractId = await _client.CreateContractAsync(token, roomId, renter, start);
        (await _client.PostJsonAsync($"/api/v1/contracts/{contractId}/activate",
            new { handoverReadings = new[] { new { meterId, value = (decimal?)null } } }, token)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await _client.PostJsonAsync($"/api/v1/contracts/{contractId}/liquidation/start", new
        {
            actualEndDate = today, reason = "MutualAgreement", finalReadings = new[] { new { meterId, value = (decimal?)150 } }
        }, token)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        await _client.SettleAndCompleteAsync(token, contractId);

        var saved = await _client.PutJsonAsync($"/api/v1/properties/{propertyId}/meter-readings",
            new { readings = new[] { new { meterId, contractId, closingPeriodStart = start, readingDate = today, value = 160 } } }, token);
        saved.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        var problem = await saved.ReadAsync<JsonElement>();
        problem.GetProperty("code").GetString().Should().Be("READINGS_INVALID");
        problem.GetProperty("rowErrors").EnumerateArray().Single().GetProperty("code").GetString().Should().Be("CONTRACT_NOT_ACTIVE");
        var readings = await (await _client.GetAsync($"/api/v1/meters/{meterId}/readings", token)).ReadAsync<List<JsonElement>>();
        readings.Should().NotContain(r => r.GetProperty("kind").GetString() == "Periodic", "HĐ đã kết thúc — không ghi số lạc cho người vào sau");
    }
}
