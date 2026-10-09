using System.Net;
using System.Text.Json;
using renting_room.IntegrationTests.Infrastructure;

namespace renting_room.IntegrationTests;

/// <summary>M06 đợt 1: công tơ theo phòng, chỉ số nhận phòng khi kích hoạt, chỉ số cuối khi thanh lý, thay công tơ phiên bản.</summary>
[Collection(ApiCollection.Name)]
public sealed class MeterTests(ApiFactory factory)
{
    private readonly HttpClient _client = factory.CreateClient();

    private async Task<List<JsonElement>> MetersAsync(string token, Guid roomId, bool includeRemoved = false) =>
        await (await _client.GetAsync($"/api/v1/rooms/{roomId}/meters?includeRemoved={includeRemoved}", token)).ReadAsync<List<JsonElement>>();

    private async Task<List<JsonElement>> ReadingsAsync(string token, Guid meterId) =>
        await (await _client.GetAsync($"/api/v1/meters/{meterId}/readings", token)).ReadAsync<List<JsonElement>>();

    [Fact]
    public async Task Install_OnlyMeteredFee_OneActivePerRoomAndFee_BlocksArchive()
    {
        var owner = await _client.CreateActiveOwnerAsync();
        var token = owner.Tokens.AccessToken;
        var today = TestData.Today(factory);
        var propertyId = await _client.CreatePropertyAsync(token);
        var roomId = await _client.CreateRoomAsync(token, propertyId);
        var electricity = await _client.FeeIdAsync(token, propertyId, "Điện");

        await _client.InstallMeterAsync(token, roomId, electricity, today, 1250);
        var meters = await MetersAsync(token, roomId);
        meters.Should().ContainSingle();
        meters[0].GetProperty("feeTypeName").GetString().Should().Be("Điện");
        meters[0].GetProperty("latestReading").GetProperty("value").GetDecimal().Should().Be(1250);

        var duplicate = await _client.PostJsonAsync($"/api/v1/rooms/{roomId}/meters", new { feeTypeId = electricity, installedDate = today, initialValue = 0 }, token);
        (await duplicate.ReadProblemCodeAsync()).Should().Be("METER_ALREADY_ACTIVE");

        var wifi = await (await _client.PostJsonAsync($"/api/v1/properties/{propertyId}/fee-types",
            new { name = "Mạng", group = "Service", chargeBasis = "PerRoom", unit = "phòng", autoAttach = false }, token)).ReadIdAsync();
        (await (await _client.PostJsonAsync($"/api/v1/rooms/{roomId}/meters", new { feeTypeId = wifi, installedDate = today, initialValue = 0 }, token))
            .ReadProblemCodeAsync()).Should().Be("FEE_NOT_METERED");

        // Còn công tơ ⇒ không ngừng dùng khoản Điện được.
        (await (await _client.PostJsonAsync($"/api/v1/fee-types/{electricity}/archive", null, token)).ReadProblemCodeAsync())
            .Should().Be("FEE_HAS_ACTIVE_METERS");
    }

    [Fact]
    public async Task Activation_RequiresHandoverReading_UseLatestOrEnterHigher_CanCorrectLater()
    {
        var owner = await _client.CreateActiveOwnerAsync();
        var token = owner.Tokens.AccessToken;
        var today = TestData.Today(factory);
        var propertyId = await _client.CreatePropertyAsync(token);
        var roomId = await _client.CreateRoomAsync(token, propertyId);
        var electricity = await _client.InstallMeterAsync(token, roomId, await _client.FeeIdAsync(token, propertyId, "Điện"), today.AddDays(-30), 100);
        var water = await _client.InstallMeterAsync(token, roomId, await _client.FeeIdAsync(token, propertyId, "Nước"), today.AddDays(-30), 20);
        var contractId = await _client.CreateContractAsync(token, roomId, await _client.CreateRenterAsync(token), today);

        var missing = await _client.PostJsonAsync($"/api/v1/contracts/{contractId}/activate",
            new { handoverReadings = new[] { new { meterId = electricity, value = (decimal?)108 } } }, token);
        var missingBody = await missing.Content.ReadAsStringAsync();
        (await missing.ReadProblemCodeAsync()).Should().Be("HANDOVER_READING_REQUIRED");
        JsonDocument.Parse(missingBody).RootElement.GetProperty("meterIds").EnumerateArray()
            .Select(e => e.GetGuid()).Should().Equal(water);

        var lower = await _client.PostJsonAsync($"/api/v1/contracts/{contractId}/activate", new
        {
            handoverReadings = new[] { new { meterId = electricity, value = (decimal?)99 }, new { meterId = water, value = (decimal?)null } }
        }, token);
        (await lower.ReadProblemCodeAsync()).Should().Be("READING_NOT_MONOTONIC");

        (await _client.PostJsonAsync($"/api/v1/contracts/{contractId}/activate", new
        {
            handoverReadings = new[] { new { meterId = electricity, value = (decimal?)108 }, new { meterId = water, value = (decimal?)null } }
        }, token)).StatusCode.Should().Be(HttpStatusCode.NoContent);

        var electricityHandover = (await ReadingsAsync(token, electricity)).First();
        electricityHandover.GetProperty("kind").GetString().Should().Be("Handover");
        electricityHandover.GetProperty("value").GetDecimal().Should().Be(108, "người cũ đi ở 100, sửa chữa dùng 8");
        (await ReadingsAsync(token, water)).First().GetProperty("value").GetDecimal().Should().Be(20, "chọn Dùng số mới nhất");

        var corrected = await _client.PutJsonAsync($"/api/v1/meter-readings/{electricityHandover.GetProperty("id").GetGuid()}",
            new { value = 105, note = "Đọc lại" }, token);
        corrected.StatusCode.Should().Be(HttpStatusCode.OK);
        (await corrected.ReadAsync<JsonElement>()).GetProperty("value").GetDecimal().Should().Be(105);
    }

    [Fact]
    public async Task ReplaceMeter_KeepsHistory_CompleteLiquidationRequiresFinalReading()
    {
        var owner = await _client.CreateActiveOwnerAsync();
        var token = owner.Tokens.AccessToken;
        var today = TestData.Today(factory);
        // Khu chốt đúng ngày bắt đầu ⇒ cả thời gian ở nằm trong 1 kỳ (không phải lập phiếu kỳ trước — BL-BR-21).
        var start = TestData.StartWithinOnePeriod(today, 20);
        var propertyId = await _client.CreatePropertyAsync(token, anchorDay: start.Day);
        var roomId = await _client.CreateRoomAsync(token, propertyId);
        var electricity = await _client.FeeIdAsync(token, propertyId, "Điện");
        await _client.PostJsonAsync($"/api/v1/fee-types/{electricity}/prices", new { effectiveFrom = today.AddDays(-60), unitPrice = 3500 }, token);
        var oldMeter = await _client.InstallMeterAsync(token, roomId, electricity, today.AddDays(-30), 1250);
        var contractId = await _client.CreateContractAsync(token, roomId, await _client.CreateRenterAsync(token), start);
        (await _client.PostJsonAsync($"/api/v1/contracts/{contractId}/activate",
            new { handoverReadings = new[] { new { meterId = oldMeter, value = (decimal?)null } } }, token)).StatusCode.Should().Be(HttpStatusCode.NoContent);

        // Giữa kỳ thay công tơ: số cuối công tơ cũ 1.320, công tơ mới bắt đầu từ 0.
        var replaced = await _client.PostJsonAsync($"/api/v1/meters/{oldMeter}/replace",
            new { date = today.AddDays(-10), oldFinalValue = 1320, newSerialNo = "E-NEW", newInitialValue = 0, note = "Công tơ cũ hỏng" }, token);
        replaced.StatusCode.Should().Be(HttpStatusCode.Created, await replaced.Content.ReadAsStringAsync());
        var newMeter = await replaced.ReadIdAsync();
        (await MetersAsync(token, roomId)).Select(m => m.GetProperty("id").GetGuid()).Should().Equal(newMeter);
        var all = await MetersAsync(token, roomId, includeRemoved: true);
        all.Should().HaveCount(2);
        all.Single(m => m.GetProperty("id").GetGuid() == oldMeter).GetProperty("replacedByMeterId").GetGuid().Should().Be(newMeter);

        await _client.PostJsonAsync($"/api/v1/contracts/{contractId}/liquidation/start", new { actualEndDate = today, reason = "MutualAgreement" }, token);
        // Chỉ số cuối nhập khi lập phiếu quyết toán (MT-UC-05).
        (await (await _client.PostJsonAsync($"/api/v1/contracts/{contractId}/liquidation/complete", null, token)).ReadProblemCodeAsync())
            .Should().Be("FINAL_READING_REQUIRED");
        (await (await _client.PostJsonAsync($"/api/v1/contracts/{contractId}/final-invoice", new { finalReadings = (object?)null }, token))
            .ReadProblemCodeAsync()).Should().Be("FINAL_READING_REQUIRED");
        await _client.SettleAndCompleteAsync(token, contractId, new[] { new { meterId = newMeter, value = (decimal?)45 } });

        (await ReadingsAsync(token, newMeter)).First().GetProperty("kind").GetString().Should().Be("Final");
    }

    [Fact]
    public async Task ActiveContract_InRoomWithoutElectricityMeter_IsWarned()
    {
        var owner = await _client.CreateActiveOwnerAsync();
        var token = owner.Tokens.AccessToken;
        var (_, _, _, contractId) = await _client.CreateActiveContractAsync(token, TestData.Today(factory));

        var detail = await (await _client.GetAsync($"/api/v1/contracts/{contractId}", token)).ReadAsync<JsonElement>();
        detail.GetProperty("warnings").EnumerateArray().Select(w => w.GetProperty("code").GetString()).Should().Contain("ROOM_WITHOUT_METER");
        detail.GetProperty("utilityPrices").EnumerateArray().Should().BeEmpty("phòng chưa có công tơ ⇒ không có điện nước theo công tơ");
    }
}
