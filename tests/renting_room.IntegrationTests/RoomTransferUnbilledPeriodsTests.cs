using System.Net;
using System.Text.Json;
using renting_room.IntegrationTests.Infrastructure;

namespace renting_room.IntegrationTests;

/// <summary>
/// 28/10/2026 — M-09 (M05 CT-BR-14): chuyển phòng khi có kỳ đã kết thúc mà chưa có hóa đơn. Khu thu sau ⇒ chặn (tạo hóa đơn hoặc bỏ qua kỳ rỗng), khu
/// thu trước ⇒ chỉ cảnh báo. Hợp đồng bắt đầu từ đầu tháng của 2 tháng trước, khu chốt ngày 1 ⇒ 2 kỳ đã kết thúc trước hôm nay.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class RoomTransferUnbilledPeriodsTests(ApiFactory factory)
{
    private readonly HttpClient _client = factory.CreateClient();

    private sealed record Setup(string Token, Guid PropertyId, Guid RoomA, Guid RoomB, Guid MeterA, Guid ContractId, DateOnly Start, DateOnly Today);

    private async Task<Setup> ArrangeAsync(string chargeMode)
    {
        var token = (await _client.CreateActiveOwnerAsync()).Tokens.AccessToken;
        var today = TestData.Today(factory);
        var start = new DateOnly(today.Year, today.Month, 1).AddMonths(-2);
        var propertyId = await _client.CreatePropertyAsync(token, anchorDay: 1, chargeMode: chargeMode);
        var electricity = await _client.FeeIdAsync(token, propertyId, "Điện");
        await _client.PostJsonAsync($"/api/v1/fee-types/{electricity}/prices", new { effectiveFrom = start.AddDays(-30), unitPrice = 3500 }, token);
        var roomA = await _client.CreateRoomAsync(token, propertyId, "A1");
        var roomB = await _client.CreateRoomAsync(token, propertyId, "A2");
        var meterA = await _client.InstallMeterAsync(token, roomA, electricity, start.AddDays(-1), 100);
        await _client.InstallMeterAsync(token, roomB, electricity, start.AddDays(-1), 500);
        var renter = await _client.CreateRenterAsync(token);
        var contractId = await _client.CreateContractAsync(token, roomA, renter, start);
        (await _client.PostJsonAsync($"/api/v1/contracts/{contractId}/activate",
            new { handoverReadings = new[] { new { meterId = meterA, value = (decimal?)null } } }, token)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        return new Setup(token, propertyId, roomA, roomB, meterA, contractId, start, today);
    }

    private Task<HttpResponseMessage> TransferAsync(Setup s, bool? skipUnbilledPeriods = null) =>
        _client.PostJsonAsync($"/api/v1/contracts/{s.ContractId}/transfer-room", new
        {
            toRoomId = s.RoomB, oldRoomReadings = new[] { new { meterId = s.MeterA, value = (decimal?)170 } }, newRoomReadings = (object?)null,
            monthlyRent = (decimal?)null, note = (string?)null, skipUnbilledPeriods
        }, s.Token);

    private Task<HttpResponseMessage> SavePeriodicAsync(Setup s, DateOnly closingPeriodStart, decimal value) =>
        _client.PutJsonAsync($"/api/v1/properties/{s.PropertyId}/meter-readings", new
        {
            readings = new[] { new { meterId = s.MeterA, contractId = s.ContractId, closingPeriodStart, readingDate = closingPeriodStart.AddMonths(1).AddDays(-1), value } }
        }, s.Token);

    private async Task<Guid> RoomOfAsync(Setup s) =>
        (await (await _client.GetAsync($"/api/v1/contracts/{s.ContractId}", s.Token)).ReadAsync<JsonElement>()).GetProperty("roomId").GetGuid();

    [Fact]
    public async Task Postpaid_TransferWithUnbilledEndedPeriods_IsRefused_UntilBilled()
    {
        var s = await ArrangeAsync("Postpaid");

        var refused = await TransferAsync(s);
        refused.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        var problem = await refused.ReadAsync<JsonElement>();
        problem.GetProperty("code").GetString().Should().Be("PREVIOUS_PERIOD_NOT_BILLED");
        problem.GetProperty("unbilledPeriods").EnumerateArray().Select(p => p.GetProperty("start").GetString())
            .Should().Equal($"{s.Start:yyyy-MM-dd}", $"{s.Start.AddMonths(1):yyyy-MM-dd}");
        (await RoomOfAsync(s)).Should().Be(s.RoomA, "bị chặn ⇒ chưa chuyển");

        // Lập hóa đơn 2 kỳ đã qua (nháp là đủ) rồi chuyển.
        (await SavePeriodicAsync(s, s.Start, 130)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await SavePeriodicAsync(s, s.Start.AddMonths(1), 160)).StatusCode.Should().Be(HttpStatusCode.OK);
        foreach (var month in new[] { s.Start, s.Start.AddMonths(1) })
        {
            var generated = await (await _client.PostJsonAsync("/api/v1/invoices/generate",
                new { propertyId = s.PropertyId, billingMonth = $"{month:yyyy-MM}" }, s.Token)).ReadAsync<JsonElement>();
            generated.GetProperty("created").GetInt32().Should().Be(1, generated.ToString());
        }

        var moved = await TransferAsync(s);
        moved.StatusCode.Should().Be(HttpStatusCode.OK, await moved.Content.ReadAsStringAsync());
        (await RoomOfAsync(s)).Should().Be(s.RoomB);
    }

    [Fact]
    public async Task Postpaid_TransferSkippingUnbilledPeriods_WritesBillingGap()
    {
        var s = await ArrangeAsync("Postpaid");

        var moved = await TransferAsync(s, skipUnbilledPeriods: true);
        moved.StatusCode.Should().Be(HttpStatusCode.OK, await moved.Content.ReadAsStringAsync());
        (await RoomOfAsync(s)).Should().Be(s.RoomB);
        var gaps = await (await _client.GetAsync($"/api/v1/contracts/{s.ContractId}/billing-gaps", s.Token)).ReadAsync<JsonElement>();
        var gap = gaps.EnumerateArray().Single();
        gap.GetProperty("fromDate").GetString().Should().Be($"{s.Start:yyyy-MM-dd}");
        gap.GetProperty("toDate").GetString().Should().Be($"{s.Start.AddMonths(2).AddDays(-1):yyyy-MM-dd}");
        gap.GetProperty("periodCount").GetInt32().Should().Be(2);
    }

    [Fact]
    public async Task Postpaid_TransferSkippingPeriods_ThenBackSameDay_RemovesTheGapItCreated()
    {
        var s = await ArrangeAsync("Postpaid");
        (await TransferAsync(s, skipUnbilledPeriods: true)).StatusCode.Should().Be(HttpStatusCode.OK);

        var back = await _client.PostJsonAsync($"/api/v1/contracts/{s.ContractId}/transfer-room", new { toRoomId = s.RoomA }, s.Token);
        back.StatusCode.Should().Be(HttpStatusCode.OK, await back.Content.ReadAsStringAsync());
        (await RoomOfAsync(s)).Should().Be(s.RoomA);
        (await (await _client.GetAsync($"/api/v1/contracts/{s.ContractId}/billing-gaps", s.Token)).ReadAsync<JsonElement>()).GetArrayLength()
            .Should().Be(0, "chuyển nhầm xóa như chưa từng chuyển ⇒ kỳ không còn bị bỏ qua, vẫn lập hóa đơn được");
        (await TransferAsync(s)).StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity, "kỳ đã qua lại phải lập hóa đơn");
    }

    [Fact]
    public async Task Prepaid_TransferWithUnbilledEndedPeriods_OnlyWarns()
    {
        var s = await ArrangeAsync("Prepaid");

        var moved = await TransferAsync(s);
        moved.StatusCode.Should().Be(HttpStatusCode.OK, await moved.Content.ReadAsStringAsync());
        (await RoomOfAsync(s)).Should().Be(s.RoomB);
        (await moved.ReadAsync<JsonElement>()).GetProperty("warnings").EnumerateArray().Select(w => w.GetProperty("code").GetString())
            .Should().Contain("PREVIOUS_PERIOD_NOT_BILLED");
    }

    [Fact]
    public async Task Postpaid_TransferMidFirstPeriod_IsNotChecked()
    {
        var token = (await _client.CreateActiveOwnerAsync()).Tokens.AccessToken;
        var today = TestData.Today(factory);
        var start = TestData.StartWithinOnePeriod(today, 10);
        var propertyId = await _client.CreatePropertyAsync(token, anchorDay: start.Day, chargeMode: "Postpaid");
        var roomA = await _client.CreateRoomAsync(token, propertyId, "A1");
        var roomB = await _client.CreateRoomAsync(token, propertyId, "A2");
        var renter = await _client.CreateRenterAsync(token);
        var contractId = await _client.CreateContractAsync(token, roomA, renter, start);
        (await _client.PostJsonAsync($"/api/v1/contracts/{contractId}/activate", null, token)).StatusCode.Should().Be(HttpStatusCode.NoContent);

        var moved = await _client.PostJsonAsync($"/api/v1/contracts/{contractId}/transfer-room", new { toRoomId = roomB }, token);
        moved.StatusCode.Should().Be(HttpStatusCode.OK, await moved.Content.ReadAsStringAsync());
    }
}
