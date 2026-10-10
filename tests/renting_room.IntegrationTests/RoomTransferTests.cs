using System.Net;
using System.Text.Json;
using renting_room.IntegrationTests.Infrastructure;

namespace renting_room.IntegrationTests;

/// <summary>M05 CT-UC-12 / CT-BR-14, 47: chuyển phòng cùng khu — HĐ giữ nguyên, điện nước kỳ chuyển cộng 2 phòng.</summary>
[Collection(ApiCollection.Name)]
public sealed class RoomTransferTests(ApiFactory factory)
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task TransferMidMonth_KeepsContract_RoomStatusesSwap_InvoiceSumsBothRooms_BlocksInvalidTargets()
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
        var roomBusy = await _client.CreateRoomAsync(token, propertyId, "A3");
        var meterA = await _client.InstallMeterAsync(token, roomA, electricity, month.AddDays(-1), 100);
        var meterB = await _client.InstallMeterAsync(token, roomB, electricity, month.AddDays(-1), 500);
        var renter = await _client.CreateRenterAsync(token);
        var contractId = await (await _client.PostJsonAsync("/api/v1/contracts", new
        {
            roomId = roomA, contract = new { representativeRenterId = renter, startDate = month, monthlyRent = 3_000_000, depositAmount = 0, occupants = new[] { new { renterId = renter } } }
        }, token)).ReadIdAsync();
        (await _client.PostJsonAsync($"/api/v1/contracts/{contractId}/activate",
            new { handoverReadings = new[] { new { meterId = meterA, value = (decimal?)null } } }, token)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        var busy = await _client.CreateContractAsync(token, roomBusy, await _client.CreateRenterAsync(token), month);
        (await _client.PostJsonAsync($"/api/v1/contracts/{busy}/activate", null, token)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        var otherProperty = await _client.CreateRoomAsync(token, await _client.CreatePropertyAsync(token, anchorDay: 1), "B1");

        var date = month.AddDays(10);
        Task<HttpResponseMessage> TransferAsync(Guid to, DateOnly on) => _client.PostJsonAsync($"/api/v1/contracts/{contractId}/transfer-room", new
        {
            toRoomId = to, date = on, oldRoomReadings = new[] { new { meterId = meterA, value = (decimal?)130 } }, newRoomReadings = (object?)null,
            monthlyRent = (decimal?)3_200_000, note = (string?)null
        }, token);
        (await (await TransferAsync(roomA, date)).ReadProblemCodeAsync()).Should().Be("ROOM_TRANSFER_SAME_ROOM");
        (await (await TransferAsync(roomBusy, date)).ReadProblemCodeAsync()).Should().Be("ROOM_TRANSFER_ROOM_UNAVAILABLE");
        (await (await TransferAsync(otherProperty, date)).ReadProblemCodeAsync()).Should().Be("ROOM_TRANSFER_OTHER_PROPERTY");

        var moved = await TransferAsync(roomB, date);
        moved.StatusCode.Should().Be(HttpStatusCode.OK, await moved.Content.ReadAsStringAsync());
        (await moved.ReadAsync<JsonElement>()).GetProperty("warnings")[0].GetProperty("code").GetString().Should().Be("RESIDENCE_ROOM_CHANGED");

        var detail = await (await _client.GetAsync($"/api/v1/contracts/{contractId}", token)).ReadAsync<JsonElement>();
        detail.GetProperty("roomId").GetGuid().Should().Be(roomB, "HĐ giữ nguyên, chỉ đổi phòng");
        detail.GetProperty("roomSince").GetString().Should().Be($"{date:yyyy-MM-dd}");
        detail.GetProperty("roomMoves")[0].GetProperty("roomCode").GetString().Should().Be("A1");
        (await (await _client.GetAsync($"/api/v1/rooms/{roomA}", token)).ReadAsync<JsonElement>()).GetProperty("status").GetString().Should().Be("Vacant");
        (await (await _client.GetAsync($"/api/v1/rooms/{roomB}", token)).ReadAsync<JsonElement>()).GetProperty("status").GetString().Should().Be("Occupied");

        // Cuối tháng: công tơ phòng mới 520 ⇒ điện = (130 − 100) phòng cũ + (520 − 500) phòng mới = 50 kWh.
        var sheet = await (await _client.GetAsync($"/api/v1/properties/{propertyId}/meter-reading-sheet?billingMonth={month:yyyy-MM}", token))
            .ReadAsync<JsonElement>();
        var row = sheet.GetProperty("rows").EnumerateArray().Single(r => r.GetProperty("contractId").GetGuid() == contractId);
        row.GetProperty("meterId").GetGuid().Should().Be(meterB);
        row.GetProperty("previous").GetProperty("value").GetDecimal().Should().Be(500, "chỉ số nhận phòng mới");
        (await _client.PutJsonAsync($"/api/v1/properties/{propertyId}/meter-readings", new
        {
            readings = new[] { new { meterId = meterB, contractId, closingPeriodStart = month, readingDate = month.AddMonths(1), value = 520m } }
        }, token)).StatusCode.Should().Be(HttpStatusCode.OK);
        await _client.PostJsonAsync("/api/v1/invoices/generate", new { propertyId, billingMonth = $"{month:yyyy-MM}", roomIds = new[] { roomB } }, token);
        var page = await (await _client.GetAsync($"/api/v1/invoices?contractId={contractId}", token)).ReadAsync<JsonElement>();
        var invoice = await (await _client.GetAsync($"/api/v1/invoices/{page.GetProperty("items")[0].GetProperty("id").GetGuid()}", token))
            .ReadAsync<JsonElement>();
        invoice.GetProperty("summary").GetProperty("roomCode").GetString().Should().Be("A2");
        var metered = invoice.GetProperty("lines").EnumerateArray().Single(l => l.GetProperty("type").GetString() == "Metered");
        metered.GetProperty("quantity").GetDecimal().Should().Be(50);
        metered.GetProperty("segments").GetArrayLength().Should().Be(2);
        invoice.GetProperty("lines").EnumerateArray().Single(l => l.GetProperty("type").GetString() == "Rent").GetProperty("amount").GetDecimal()
            .Should().Be(3_200_000, "giá mới áp từ kỳ chưa chốt đầu tiên — lúc chuyển kỳ này chưa có phiếu");

        // Đã lập phiếu điện nước tới cuối tháng ⇒ không chuyển tiếp được với ngày trong tháng đó.
        (await (await _client.PostJsonAsync($"/api/v1/contracts/{contractId}/transfer-room", new
        {
            toRoomId = roomA, date = month.AddDays(20), oldRoomReadings = new[] { new { meterId = meterB, value = (decimal?)510 } }
        }, token)).ReadProblemCodeAsync()).Should().Be("ROOM_TRANSFER_ALREADY_BILLED");
    }
}
