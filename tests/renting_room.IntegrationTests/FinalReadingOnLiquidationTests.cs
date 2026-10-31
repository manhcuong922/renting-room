using System.Net;
using System.Text.Json;
using renting_room.IntegrationTests.Infrastructure;

namespace renting_room.IntegrationTests;

/// <summary>
/// 28/10/2026 — H-02 (M05 CT-BR-11/12, CT-BR-02, CT-UC-21; M06 MT-BR-13): bấm thanh lý = người thuê đã rời, phòng coi như trống ⇒ chỉ số cuối
/// nhập ngay lúc bắt đầu thanh lý. Khu thu sau, điện 3.800đ/kWh, công tơ lắp ở 1.000.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class FinalReadingOnLiquidationTests(ApiFactory factory)
{
    private readonly HttpClient _client = factory.CreateClient();

    private sealed record Setup(string Token, Guid PropertyId, Guid RoomId, Guid ContractId, Guid MeterId, DateOnly Start, DateOnly Today);

    private async Task<Setup> ArrangeAsync(int billingDelayDays = 0, int stayDays = 10)
    {
        var token = (await _client.CreateActiveOwnerAsync()).Tokens.AccessToken;
        var today = TestData.Today(factory);
        var start = billingDelayDays > 0 ? today.AddDays(-stayDays) : TestData.StartWithinOnePeriod(today, stayDays);
        var propertyId = await _client.CreatePropertyAsync(token, anchorDay: start.Day, chargeMode: "Postpaid");
        var electricity = await _client.FeeIdAsync(token, propertyId, "Điện");
        await _client.PostJsonAsync($"/api/v1/fee-types/{electricity}/prices", new { effectiveFrom = start.AddDays(-30), unitPrice = 3800 }, token);
        var roomId = await _client.CreateRoomAsync(token, propertyId);
        var meterId = await _client.InstallMeterAsync(token, roomId, electricity, start.AddDays(-5), 1000);
        var renter = await _client.CreateRenterAsync(token);
        var created = await _client.PostJsonAsync("/api/v1/contracts", new
        {
            roomId,
            contract = new
            {
                representativeRenterId = renter, startDate = start, billingStartDate = billingDelayDays > 0 ? start.AddDays(billingDelayDays) : (DateOnly?)null,
                monthlyRent = 3_000_000, depositAmount = 0, occupants = new[] { new { renterId = renter } }
            }
        }, token);
        created.StatusCode.Should().Be(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        var contractId = await created.ReadIdAsync();
        (await _client.PostJsonAsync($"/api/v1/contracts/{contractId}/activate",
            new { handoverReadings = new[] { new { meterId, value = (decimal?)null } } }, token)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        return new Setup(token, propertyId, roomId, contractId, meterId, start, today);
    }

    private Task<HttpResponseMessage> StartAsync(Setup s, DateOnly endDate, decimal? finalValue = null) =>
        _client.PostJsonAsync($"/api/v1/contracts/{s.ContractId}/liquidation/start", new
        {
            actualEndDate = endDate, reason = "MutualAgreement",
            finalReadings = finalValue is null ? null : new[] { new { meterId = s.MeterId, value = finalValue } }
        }, s.Token);

    private async Task<List<JsonElement>> ReadingsAsync(Setup s, string kind) =>
        (await (await _client.GetAsync($"/api/v1/meters/{s.MeterId}/readings", s.Token)).ReadAsync<List<JsonElement>>())
        .Where(r => r.GetProperty("kind").GetString() == kind).ToList();

    private async Task<string?> StatusAsync(Guid contractId, string token) =>
        (await (await _client.GetAsync($"/api/v1/contracts/{contractId}", token)).ReadAsync<JsonElement>()).GetProperty("status").GetString();

    [Fact]
    public async Task StartLiquidation_PastOrTodayEndDate_RequiresFinalReadings_AndCancelVoidsThem()
    {
        var s = await ArrangeAsync();

        var missing = await StartAsync(s, s.Today);
        (await missing.ReadProblemCodeAsync()).Should().Be("FINAL_READING_REQUIRED");
        (await StatusAsync(s.ContractId, s.Token)).Should().Be("Active", "thiếu số cuối ⇒ chưa thanh lý");

        var started = await StartAsync(s, s.Today, 1080);
        started.StatusCode.Should().Be(HttpStatusCode.NoContent, await started.Content.ReadAsStringAsync());
        var final = (await ReadingsAsync(s, "Final")).Should().ContainSingle().Subject;
        final.GetProperty("value").GetDecimal().Should().Be(1080);
        final.GetProperty("readingDate").GetString().Should().Be($"{s.Today:yyyy-MM-dd}");

        (await _client.PostJsonAsync($"/api/v1/contracts/{s.ContractId}/liquidation/cancel", null, s.Token)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await ReadingsAsync(s, "Final")).Should().BeEmpty("hủy thanh lý ⇒ hủy số cuối đã ghi");
    }

    [Fact]
    public async Task NewTenant_UsingLatestReading_StartsFromOldTenantsFinalReading()
    {
        var s = await ArrangeAsync();
        (await StartAsync(s, s.Today, 1080)).StatusCode.Should().Be(HttpStatusCode.NoContent);

        var newRenter = await _client.CreateRenterAsync(s.Token);
        var newContract = await _client.CreateContractAsync(s.Token, s.RoomId, newRenter, s.Today.AddDays(1));
        var activated = await _client.PostJsonAsync($"/api/v1/contracts/{newContract}/activate",
            new { handoverReadings = new[] { new { meterId = s.MeterId, value = (decimal?)null } } }, s.Token);
        activated.StatusCode.Should().Be(HttpStatusCode.NoContent, await activated.Content.ReadAsStringAsync());
        (await ReadingsAsync(s, "Handover")).Should().Contain(r => r.GetProperty("contractId").GetGuid() == newContract && r.GetProperty("value").GetDecimal() == 1080,
            "\"Dùng số mới nhất\" là số cuối người cũ, không phải số chốt kỳ cũ");

        // Hóa đơn trả phòng của người cũ dùng sẵn số cuối: 1.000 → 1.080 = 80 kWh.
        var final = await _client.PostJsonAsync($"/api/v1/contracts/{s.ContractId}/final-invoice", new { finalReadings = (object?)null }, s.Token);
        final.StatusCode.Should().Be(HttpStatusCode.Created, await final.Content.ReadAsStringAsync());
        var detail = await final.ReadAsync<JsonElement>();
        detail.GetProperty("lines").EnumerateArray().Single(l => l.GetProperty("type").GetString() == "Metered")
            .GetProperty("amount").GetDecimal().Should().Be(80 * 3800);
    }

    [Fact]
    public async Task StartLiquidation_FutureEndDate_DoesNotNeedFinalReadings()
    {
        var s = await ArrangeAsync();

        var started = await StartAsync(s, s.Today.AddDays(5));
        started.StatusCode.Should().Be(HttpStatusCode.NoContent, await started.Content.ReadAsStringAsync());
        (await StatusAsync(s.ContractId, s.Token)).Should().Be("Liquidating");
        (await ReadingsAsync(s, "Final")).Should().BeEmpty("chưa tới ngày trả phòng ⇒ nhập sau");
    }

    [Fact]
    public async Task StartLiquidation_EndBeforeBillingStart_DoesNotNeedFinalReadings()
    {
        var s = await ArrangeAsync(billingDelayDays: 20, stayDays: 30);

        var started = await StartAsync(s, s.Start.AddDays(5));
        started.StatusCode.Should().Be(HttpStatusCode.NoContent, await started.Content.ReadAsStringAsync());
        (await ReadingsAsync(s, "Final")).Should().BeEmpty("chưa tới ngày tính tiền ⇒ chưa có chỉ số nhận phòng để đối chiếu");
    }

    [Fact]
    public async Task Resign_Sign_RecordsOldContractFinalReading_EqualToNewHandover()
    {
        var s = await ArrangeAsync();
        var renter = (await (await _client.GetAsync($"/api/v1/contracts/{s.ContractId}", s.Token)).ReadAsync<JsonElement>())
            .GetProperty("representativeRenterId").GetGuid();
        var resigned = await _client.PostJsonAsync($"/api/v1/contracts/{s.ContractId}/re-sign",
            new { handoverDate = s.Today, representativeRenterId = renter }, s.Token);
        resigned.StatusCode.Should().Be(HttpStatusCode.Created, await resigned.Content.ReadAsStringAsync());
        var draft = await resigned.ReadIdAsync();

        var signed = await _client.PostJsonAsync($"/api/v1/contracts/{draft}/activate",
            new { handoverReadings = new[] { new { meterId = s.MeterId, value = (decimal?)1050 } } }, s.Token);
        signed.StatusCode.Should().Be(HttpStatusCode.NoContent, await signed.Content.ReadAsStringAsync());

        var final = (await ReadingsAsync(s, "Final")).Should().ContainSingle().Subject;
        final.GetProperty("contractId").GetGuid().Should().Be(s.ContractId);
        final.GetProperty("value").GetDecimal().Should().Be(1050, "cùng người, cùng thời điểm bàn giao");
        final.GetProperty("readingDate").GetString().Should().Be($"{s.Today:yyyy-MM-dd}");
    }
}
