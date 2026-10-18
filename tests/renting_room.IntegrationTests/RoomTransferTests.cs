using System.Net;
using System.Text.Json;
using renting_room.IntegrationTests.Infrastructure;

namespace renting_room.IntegrationTests;

/// <summary>
/// M05 CT-UC-12 / CT-BR-14, 47, 48: chuyển phòng cùng khu <b>từ hôm nay</b> — HĐ giữ nguyên, sản lượng phòng cũ cộng vào dòng điện nước của kỳ;
/// chuyển về phòng vừa rời trong ngày = xóa lần chuyển. Kịch bản đi rồi về phòng cũ ở các ngày khác nhau: unit test <c>InvoiceTransferUsageTests</c>.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class RoomTransferTests(ApiFactory factory)
{
    private readonly HttpClient _client = factory.CreateClient();

    private async Task<JsonElement> DetailAsync(Guid contractId, string token) =>
        await (await _client.GetAsync($"/api/v1/contracts/{contractId}", token)).ReadAsync<JsonElement>();

    private async Task<string?> RoomStatusAsync(Guid roomId, string token) =>
        (await (await _client.GetAsync($"/api/v1/rooms/{roomId}", token)).ReadAsync<JsonElement>()).GetProperty("status").GetString();

    private async Task<List<decimal>> ReadingValuesAsync(Guid meterId, string kind, string token) =>
        (await (await _client.GetAsync($"/api/v1/meters/{meterId}/readings", token)).ReadAsync<List<JsonElement>>())
        .Where(r => r.GetProperty("kind").GetString() == kind).Select(r => r.GetProperty("value").GetDecimal()).Order().ToList();

    private async Task<JsonElement> InvoiceAsync(Guid propertyId, Guid contractId, Guid roomId, DateOnly billingMonth, string token)
    {
        var generated = await (await _client.PostJsonAsync("/api/v1/invoices/generate",
            new { propertyId, billingMonth = $"{billingMonth:yyyy-MM}", roomIds = new[] { roomId } }, token)).ReadAsync<JsonElement>();
        generated.GetProperty("created").GetInt32().Should().Be(1, generated.ToString());
        var page = await (await _client.GetAsync($"/api/v1/invoices?contractId={contractId}&billingMonth={billingMonth:yyyy-MM}", token)).ReadAsync<JsonElement>();
        return await (await _client.GetAsync($"/api/v1/invoices/{page.GetProperty("items")[0].GetProperty("id").GetGuid()}", token)).ReadAsync<JsonElement>();
    }

    private static decimal Sum(JsonElement invoice, string type, string property) => invoice.GetProperty("lines").EnumerateArray()
        .Where(l => l.GetProperty("type").GetString() == type).Sum(l => l.GetProperty(property).GetDecimal());

    private Task<HttpResponseMessage> SavePeriodicAsync(Guid propertyId, Guid meterId, Guid contractId, DateOnly closingPeriodStart, DateOnly readingDate,
        decimal value, string token) =>
        _client.PutJsonAsync($"/api/v1/properties/{propertyId}/meter-readings",
            new { readings = new[] { new { meterId, contractId, closingPeriodStart, readingDate, value } } }, token);

    [Fact]
    public async Task TransferToday_KeepsContract_UsageOfBothRoomsInOneLine_BlocksInvalidTargets()
    {
        var owner = await _client.CreateActiveOwnerAsync();
        var token = owner.Tokens.AccessToken;
        var today = TestData.Today(factory);
        var start = TestData.StartWithinOnePeriod(today, 10); // kỳ hiện tại bắt đầu ~10 ngày trước ⇒ chưa quá nửa kỳ
        var propertyId = await _client.CreatePropertyAsync(token, anchorDay: start.Day, chargeMode: "Postpaid");
        var electricity = await _client.FeeIdAsync(token, propertyId, "Điện");
        await _client.PostJsonAsync($"/api/v1/fee-types/{electricity}/prices", new { effectiveFrom = start.AddDays(-30), unitPrice = 3500 }, token);
        var roomA = await _client.CreateRoomAsync(token, propertyId, "A1");
        var roomB = await _client.CreateRoomAsync(token, propertyId, "A2");
        var roomBusy = await _client.CreateRoomAsync(token, propertyId, "A3");
        var meterA = await _client.InstallMeterAsync(token, roomA, electricity, start.AddDays(-1), 100);
        var meterB = await _client.InstallMeterAsync(token, roomB, electricity, start.AddDays(-1), 500);
        var renter = await _client.CreateRenterAsync(token);
        var contractId = await (await _client.PostJsonAsync("/api/v1/contracts", new
        {
            roomId = roomA, contract = new { representativeRenterId = renter, startDate = start, monthlyRent = 3_000_000, depositAmount = 0, occupants = new[] { new { renterId = renter } } }
        }, token)).ReadIdAsync();
        (await _client.PostJsonAsync($"/api/v1/contracts/{contractId}/activate",
            new { handoverReadings = new[] { new { meterId = meterA, value = (decimal?)null } } }, token)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        var busy = await _client.CreateContractAsync(token, roomBusy, await _client.CreateRenterAsync(token), start);
        (await _client.PostJsonAsync($"/api/v1/contracts/{busy}/activate", null, token)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        var otherProperty = await _client.CreateRoomAsync(token, await _client.CreatePropertyAsync(token, anchorDay: 1), "B1");

        // Body gửi kèm ngày cũ (API trước 17/10/2026) — bị bỏ qua, ngày chuyển luôn là hôm nay.
        Task<HttpResponseMessage> TransferAsync(Guid to) => _client.PostJsonAsync($"/api/v1/contracts/{contractId}/transfer-room", new
        {
            toRoomId = to, date = today.AddDays(-5), oldRoomReadings = new[] { new { meterId = meterA, value = (decimal?)130 } },
            newRoomReadings = (object?)null, monthlyRent = (decimal?)3_200_000, note = (string?)null
        }, token);
        (await (await TransferAsync(roomA)).ReadProblemCodeAsync()).Should().Be("ROOM_TRANSFER_SAME_ROOM");
        (await (await TransferAsync(roomBusy)).ReadProblemCodeAsync()).Should().Be("ROOM_TRANSFER_ROOM_UNAVAILABLE");
        (await (await TransferAsync(otherProperty)).ReadProblemCodeAsync()).Should().Be("ROOM_TRANSFER_OTHER_PROPERTY");

        var moved = await TransferAsync(roomB);
        moved.StatusCode.Should().Be(HttpStatusCode.OK, await moved.Content.ReadAsStringAsync());
        (await moved.ReadAsync<JsonElement>()).GetProperty("warnings")[0].GetProperty("code").GetString().Should().Be("RESIDENCE_ROOM_CHANGED");

        var detail = await DetailAsync(contractId, token);
        detail.GetProperty("roomId").GetGuid().Should().Be(roomB, "HĐ giữ nguyên, chỉ đổi phòng");
        detail.GetProperty("roomSince").GetString().Should().Be($"{today:yyyy-MM-dd}", "ngày chuyển = hôm nay");
        detail.GetProperty("roomMoves")[0].GetProperty("roomCode").GetString().Should().Be("A1");
        (await RoomStatusAsync(roomA, token)).Should().Be("Occupied", "ngày chuyển tính là đang thuê ở cả 2 phòng");
        (await RoomStatusAsync(roomB, token)).Should().Be("Occupied");
        (await ReadingValuesAsync(meterA, "Final", token)).Should().Equal(130m);

        // CT-BR-47: không còn khoản chốt điện nước — sản lượng phòng cũ nằm trong dòng điện của phiếu tháng này.
        (await (await _client.GetAsync($"/api/v1/room-charges?roomId={roomA}", token)).ReadAsync<List<JsonElement>>()).Should().BeEmpty();
        var sheet = await (await _client.GetAsync($"/api/v1/properties/{propertyId}/meter-reading-sheet?billingMonth={start:yyyy-MM}", token))
            .ReadAsync<JsonElement>();
        var row = sheet.GetProperty("rows").EnumerateArray().Single(r => r.GetProperty("contractId").GetGuid() == contractId);
        row.GetProperty("meterId").GetGuid().Should().Be(meterB);
        row.GetProperty("previous").GetProperty("value").GetDecimal().Should().Be(500, "chỉ số nhận phòng mới");
        (await SavePeriodicAsync(propertyId, meterB, contractId, start, today, 520, token)).StatusCode.Should().Be(HttpStatusCode.OK);

        var invoice = await InvoiceAsync(propertyId, contractId, roomB, start, token);
        invoice.GetProperty("summary").GetProperty("roomCode").GetString().Should().Be("A2");
        var metered = invoice.GetProperty("lines").EnumerateArray().Single(l => l.GetProperty("type").GetString() == "Metered");
        metered.GetProperty("quantity").GetDecimal().Should().Be(30 + 20, "A 100 → 130 + B 500 → 520 trên một dòng");
        metered.GetProperty("amount").GetDecimal().Should().Be(50 * 3500);
        // BL-BR-05: UI hiện từng phòng dưới dòng Điện.
        metered.GetProperty("segments").EnumerateArray()
            .Select(x => (x.GetProperty("roomCode").GetString(), x.GetProperty("startValue").GetDecimal(), x.GetProperty("endValue").GetDecimal(),
                x.GetProperty("endDate").GetString()))
            .Should().Equal(("A1", 100m, 130m, $"{today:yyyy-MM-dd}"), ("A2", 500m, 520m, $"{today:yyyy-MM-dd}"));
        invoice.GetProperty("lines").EnumerateArray().Should().NotContain(l => l.GetProperty("type").GetString() == "Surcharge");
        Sum(invoice, "Rent", "amount").Should().Be(3_200_000, "ở phòng cũ chưa quá nửa kỳ ⇒ cả kỳ giá mới");

        // Đã lập phiếu cho phòng mới ⇒ chuyển lại về phòng cũ trong ngày (xóa lần chuyển) bị chặn.
        (await (await _client.PostJsonAsync($"/api/v1/contracts/{contractId}/transfer-room", new { toRoomId = roomA }, token))
            .ReadProblemCodeAsync()).Should().Be("ROOM_TRANSFER_CANCEL_BILLED");
    }

    /// <summary>
    /// CT-BR-14 (17/10/2026): chuyển nhầm — chuyển về đúng phòng vừa rời trong ngày ⇒ xóa lần chuyển: phòng, giá thuê, dịch vụ, chỉ số trở lại
    /// như chưa chuyển; chuyển lại với chỉ số đúng được. Lệnh hủy chuyển phòng riêng đã bỏ.
    /// </summary>
    [Fact]
    public async Task TransferBackSameDay_UndoesTransfer_RestoresRoomRentServicesAndReadings()
    {
        var owner = await _client.CreateActiveOwnerAsync();
        var token = owner.Tokens.AccessToken;
        var today = TestData.Today(factory);
        var start = today.AddDays(-20);
        var propertyId = await _client.CreatePropertyAsync(token, anchorDay: 1, chargeMode: "Postpaid");
        var electricity = await _client.FeeIdAsync(token, propertyId, "Điện");
        await _client.PostJsonAsync($"/api/v1/fee-types/{electricity}/prices", new { effectiveFrom = start.AddDays(-30), unitPrice = 3500 }, token);
        var wifi = await (await _client.PostJsonAsync($"/api/v1/properties/{propertyId}/fee-types", new
        {
            name = "Wifi", group = "Service", chargeBasis = "PerRoom", unit = "phòng", autoAttach = false,
            initialPrice = new { effectiveFrom = start.AddDays(-30), unitPrice = 100000 }
        }, token)).ReadIdAsync();
        var roomA = await _client.CreateRoomAsync(token, propertyId, "A1");
        var roomB = await _client.CreateRoomAsync(token, propertyId, "A2", listedRent: 4_000_000);
        var meterA = await _client.InstallMeterAsync(token, roomA, electricity, start.AddDays(-1), 100);
        var meterB = await _client.InstallMeterAsync(token, roomB, electricity, start.AddDays(-1), 500);
        var renter = await _client.CreateRenterAsync(token);
        var contractId = await (await _client.PostJsonAsync("/api/v1/contracts", new
        {
            roomId = roomA,
            contract = new
            {
                representativeRenterId = renter, startDate = start, monthlyRent = 3_500_000, depositAmount = 0,
                occupants = new[] { new { renterId = renter } }, fees = new[] { new { feeTypeId = wifi } }
            }
        }, token)).ReadIdAsync();
        (await _client.PostJsonAsync($"/api/v1/contracts/{contractId}/activate",
            new { handoverReadings = new[] { new { meterId = meterA, value = (decimal?)null } } }, token)).StatusCode.Should().Be(HttpStatusCode.NoContent);

        async Task<(List<decimal> Rents, bool HasWifi, Guid RoomId)> StateAsync()
        {
            var detail = await DetailAsync(contractId, token);
            return (detail.GetProperty("rentTerms").EnumerateArray().Select(t => t.GetProperty("monthlyRent").GetDecimal()).ToList(),
                detail.GetProperty("fees").EnumerateArray().Any(f => f.GetProperty("feeTypeId").GetGuid() == wifi),
                detail.GetProperty("roomId").GetGuid());
        }
        Task<HttpResponseMessage> TransferAsync(Guid to, decimal? final) => _client.PostJsonAsync($"/api/v1/contracts/{contractId}/transfer-room",
            new { toRoomId = to, oldRoomReadings = final is null ? null : new[] { new { meterId = meterA, value = final } }, removeFeeTypeIds = new[] { wifi } },
            token);
        (await StateAsync()).Should().BeEquivalentTo((new List<decimal> { 3_500_000 }, true, roomA));
        (await _client.PostJsonAsync($"/api/v1/contracts/{contractId}/transfer-room/cancel", null, token)).StatusCode
            .Should().Be(HttpStatusCode.NotFound, "lệnh hủy chuyển phòng riêng đã bỏ");

        // Chuyển sang B (giá niêm yết 4tr, bỏ wifi) với chỉ số gõ nhầm 1300.
        var moved = await TransferAsync(roomB, 1300);
        moved.StatusCode.Should().Be(HttpStatusCode.OK, await moved.Content.ReadAsStringAsync());
        var after = await StateAsync();
        after.Rents.Should().Contain(4_000_000);
        after.HasWifi.Should().BeFalse();
        after.RoomId.Should().Be(roomB);

        // Chuyển về A ngay trong ngày ⇒ như chưa từng chuyển.
        var back = await _client.PostJsonAsync($"/api/v1/contracts/{contractId}/transfer-room", new { toRoomId = roomA }, token);
        back.StatusCode.Should().Be(HttpStatusCode.OK, await back.Content.ReadAsStringAsync());
        (await back.ReadAsync<JsonElement>()).GetProperty("warnings").EnumerateArray().Select(w => w.GetProperty("code").GetString())
            .Should().NotContain("ROOM_TRANSFER_CANCELLED_CHECK_RENT", "giá / dịch vụ trả lại được hết");
        (await StateAsync()).Should().BeEquivalentTo((new List<decimal> { 3_500_000 }, true, roomA));
        var detail = await DetailAsync(contractId, token);
        detail.GetProperty("roomSince").GetString().Should().Be($"{start:yyyy-MM-dd}");
        detail.GetProperty("roomMoves").GetArrayLength().Should().Be(0);
        (await RoomStatusAsync(roomB, token)).Should().Be("Vacant");
        (await ReadingValuesAsync(meterA, "Final", token)).Should().BeEmpty("chỉ số cuối của lần chuyển bị hủy");
        (await ReadingValuesAsync(meterB, "Handover", token)).Should().BeEmpty("chỉ số nhận phòng mới bị hủy");

        // Chuyển lại với chỉ số đúng.
        (await TransferAsync(roomB, 130)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await StateAsync()).RoomId.Should().Be(roomB);
        (await ReadingValuesAsync(meterA, "Final", token)).Should().Equal(130m);
    }

    /// <summary>
    /// CT-BR-47 ở khu thu trước (điện nước kỳ trước nằm trên phiếu kỳ này): đã ghi chỉ số cuối kỳ trước ⇒ phiếu kỳ này tính kỳ trước ở phòng cũ,
    /// phần sau tới lúc chuyển cộng vào dòng của phiếu kỳ sau cùng phòng mới; chưa ghi ⇒ phòng cũ tính tới chỉ số lúc chuyển ngay trên phiếu
    /// kỳ này (không báo thiếu chỉ số), phiếu kỳ sau chỉ còn phòng mới.
    /// </summary>
    [Fact]
    public async Task Transfer_PrepaidProperty_OldRoomUsageAddedToInvoiceLines_NoClosingCharge()
    {
        var owner = await _client.CreateActiveOwnerAsync();
        var token = owner.Tokens.AccessToken;
        var today = TestData.Today(factory);
        var month = new DateOnly(today.Year, today.Month, 1);
        var previous = month.AddMonths(-1);
        var propertyId = await _client.CreatePropertyAsync(token, anchorDay: 1, chargeMode: "Prepaid");
        var electricity = await _client.FeeIdAsync(token, propertyId, "Điện");
        await _client.PostJsonAsync($"/api/v1/fee-types/{electricity}/prices", new { effectiveFrom = previous.AddDays(-30), unitPrice = 3500 }, token);

        async Task<(Guid Contract, Guid OldMeter, Guid NewRoom, Guid NewMeter)> TenancyAsync(string oldCode, string newCode)
        {
            var oldRoom = await _client.CreateRoomAsync(token, propertyId, oldCode);
            var newRoom = await _client.CreateRoomAsync(token, propertyId, newCode);
            var oldMeter = await _client.InstallMeterAsync(token, oldRoom, electricity, previous.AddDays(-1), 100);
            var newMeter = await _client.InstallMeterAsync(token, newRoom, electricity, previous.AddDays(-1), 500);
            var contract = await _client.CreateContractAsync(token, oldRoom, await _client.CreateRenterAsync(token), previous);
            (await _client.PostJsonAsync($"/api/v1/contracts/{contract}/activate",
                new { handoverReadings = new[] { new { meterId = oldMeter, value = (decimal?)null } } }, token)).StatusCode.Should().Be(HttpStatusCode.NoContent);
            return (contract, oldMeter, newRoom, newMeter);
        }
        async Task TransferAsync(Guid contract, Guid oldMeter, Guid newRoom, decimal final)
        {
            var moved = await _client.PostJsonAsync($"/api/v1/contracts/{contract}/transfer-room",
                new { toRoomId = newRoom, oldRoomReadings = new[] { new { meterId = oldMeter, value = (decimal?)final } } }, token);
            moved.StatusCode.Should().Be(HttpStatusCode.OK, await moved.Content.ReadAsStringAsync());
        }

        // (1) Đã ghi chỉ số cuối kỳ trước (150) rồi mới chuyển ở 165.
        var recorded = await TenancyAsync("A1", "A2");
        (await SavePeriodicAsync(propertyId, recorded.OldMeter, recorded.Contract, previous, month, 150, token)).StatusCode.Should().Be(HttpStatusCode.OK);
        await TransferAsync(recorded.Contract, recorded.OldMeter, recorded.NewRoom, 165);
        await InvoiceAsync(propertyId, recorded.Contract, recorded.NewRoom, previous, token);
        var first = await InvoiceAsync(propertyId, recorded.Contract, recorded.NewRoom, month, token);
        Sum(first, "Metered", "quantity").Should().Be(50, "100 → 150 của kỳ trước ở phòng cũ, tính trên phiếu kỳ này (thu trước)");
        first.GetProperty("lines").EnumerateArray().Should().NotContain(l => l.GetProperty("type").GetString() == "Surcharge");
        (await SavePeriodicAsync(propertyId, recorded.NewMeter, recorded.Contract, month, today, 520, token)).StatusCode.Should().Be(HttpStatusCode.OK);
        var firstNext = await InvoiceAsync(propertyId, recorded.Contract, recorded.NewRoom, month.AddMonths(1), token);
        Sum(firstNext, "Metered", "quantity").Should().Be(15 + 20, "phòng cũ 150 → 165 + phòng mới 500 → 520 trên một dòng");

        // (2) Chưa ghi chỉ số cuối kỳ trước, chuyển ở 180.
        var unrecorded = await TenancyAsync("B1", "B2");
        await TransferAsync(unrecorded.Contract, unrecorded.OldMeter, unrecorded.NewRoom, 180);
        var sheet = await (await _client.GetAsync($"/api/v1/properties/{propertyId}/meter-reading-sheet?billingMonth={month:yyyy-MM}", token))
            .ReadAsync<JsonElement>();
        sheet.GetProperty("rows").EnumerateArray().Should().NotContain(r => r.GetProperty("contractId").GetGuid() == unrecorded.Contract,
            "kỳ trước HĐ chưa ở phòng mới; phòng cũ tính tới chỉ số lúc chuyển");
        await InvoiceAsync(propertyId, unrecorded.Contract, unrecorded.NewRoom, previous, token);
        var second = await InvoiceAsync(propertyId, unrecorded.Contract, unrecorded.NewRoom, month, token);
        second.GetProperty("issues").EnumerateArray().Select(i => i.GetProperty("code").GetString()).Should().NotContain("MISSING_READING");
        Sum(second, "Metered", "quantity").Should().Be(80, "100 → 180 tới chỉ số lúc chuyển");
        (await SavePeriodicAsync(propertyId, unrecorded.NewMeter, unrecorded.Contract, month, today, 520, token)).StatusCode.Should().Be(HttpStatusCode.OK);
        var secondNext = await InvoiceAsync(propertyId, unrecorded.Contract, unrecorded.NewRoom, month.AddMonths(1), token);
        Sum(secondNext, "Metered", "quantity").Should().Be(20, "phòng cũ đã tính hết ở phiếu trước");
    }

    /// <summary>CT-BR-48: giá mới áp theo quy tắc nửa kỳ (không chia ngày), bỏ dịch vụ không dùng ở phòng mới, cảnh báo công tơ / giá niêm yết.</summary>
    [Fact]
    public async Task Transfer_RentFollowsHalfPeriodRule_ServicesCanBeDropped_WarnsOnMeterAndListedRent()
    {
        var owner = await _client.CreateActiveOwnerAsync();
        var token = owner.Tokens.AccessToken;
        var today = TestData.Today(factory);
        var start = TestData.StartWithinOnePeriod(today, 20); // kỳ hiện tại bắt đầu ~20 ngày trước ⇒ hôm nay đã quá nửa kỳ
        var nextPeriod = start.AddMonths(1);
        var propertyId = await _client.CreatePropertyAsync(token, anchorDay: start.Day, chargeMode: "Postpaid");
        var electricity = await _client.FeeIdAsync(token, propertyId, "Điện");
        await _client.PostJsonAsync($"/api/v1/fee-types/{electricity}/prices", new { effectiveFrom = start.AddDays(-30), unitPrice = 3500 }, token);
        var airCon = await (await _client.PostJsonAsync($"/api/v1/properties/{propertyId}/fee-types", new
        {
            name = "Phí điều hòa", group = "Service", chargeBasis = "PerRoom", unit = "phòng", autoAttach = false,
            initialPrice = new { effectiveFrom = start.AddDays(-30), unitPrice = 100_000 }
        }, token)).ReadIdAsync();

        async Task<Guid> ContractInAsync(Guid room)
        {
            var id = await _client.CreateContractAsync(token, room, await _client.CreateRenterAsync(token), start); // giá thuê 3,5tr
            (await _client.PostJsonAsync($"/api/v1/contracts/{id}/activate", null, token)).StatusCode.Should().Be(HttpStatusCode.NoContent);
            return id;
        }
        async Task<JsonElement> TransferAsync(Guid contract, object body)
        {
            var moved = await _client.PostJsonAsync($"/api/v1/contracts/{contract}/transfer-room", body, token);
            moved.StatusCode.Should().Be(HttpStatusCode.OK, await moved.Content.ReadAsStringAsync());
            return await moved.ReadAsync<JsonElement>();
        }
        async Task<List<(string From, decimal Rent)>> RentTermsAsync(Guid contract) =>
            (await DetailAsync(contract, token)).GetProperty("rentTerms").EnumerateArray()
            .Select(t => (t.GetProperty("effectiveFrom").GetString()!, t.GetProperty("monthlyRent").GetDecimal())).OrderBy(t => t.Item1).ToList();

        // (1) Không gửi giá: lấy giá niêm yết phòng mới (4,0tr) — đã quá nửa kỳ nên áp từ kỳ sau; bỏ phí điều hòa; phòng mới không có công tơ điện.
        var roomA = await _client.CreateRoomAsync(token, propertyId, "A1");
        var meterA = await _client.InstallMeterAsync(token, roomA, electricity, start.AddDays(-1), 100);
        var first = await _client.CreateContractAsync(token, roomA, await _client.CreateRenterAsync(token), start);
        (await _client.PostJsonAsync($"/api/v1/contracts/{first}/activate",
            new { handoverReadings = new[] { new { meterId = meterA, value = (decimal?)null } } }, token)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await _client.PutJsonAsync($"/api/v1/contracts/{first}/fees/{airCon}", new { quantity = 1 }, token)).IsSuccessStatusCode.Should().BeTrue();
        var kept = await TransferAsync(first, new
        {
            toRoomId = await _client.CreateRoomAsync(token, propertyId, "A2", listedRent: 4_000_000),
            oldRoomReadings = new[] { new { meterId = meterA, value = (decimal?)130 } }, removeFeeTypeIds = new[] { airCon }
        });
        kept.GetProperty("warnings").EnumerateArray().Select(w => w.GetProperty("code").GetString())
            .Should().Contain(["RESIDENCE_ROOM_CHANGED", "ROOM_TRANSFER_LISTED_RENT_APPLIED", "METER_MISSING_IN_NEW_ROOM"]);
        (await RentTermsAsync(first)).Should().Equal(($"{start:yyyy-MM-dd}", 3_500_000m), ($"{nextPeriod:yyyy-MM-dd}", 4_000_000m));
        (await DetailAsync(first, token)).GetProperty("fees").EnumerateArray()
            .Should().NotContain(f => f.GetProperty("feeTypeId").GetGuid() == airCon, "dịch vụ theo phòng không mang sang phòng mới");

        // (2) Gửi giá mới, quá nửa kỳ ⇒ kỳ này vẫn giá cũ, giá mới từ kỳ sau.
        var second = await ContractInAsync(await _client.CreateRoomAsync(token, propertyId, "B1"));
        await TransferAsync(second, new { toRoomId = await _client.CreateRoomAsync(token, propertyId, "B2"), monthlyRent = 4_000_000 });
        (await RentTermsAsync(second)).Should().Equal(($"{start:yyyy-MM-dd}", 3_500_000m), ($"{nextPeriod:yyyy-MM-dd}", 4_000_000m));

        // (3) Chủ trọ chọn áp ngay kỳ này dù đã quá nửa kỳ ⇒ cả kỳ giá mới.
        var third = await ContractInAsync(await _client.CreateRoomAsync(token, propertyId, "C1"));
        await TransferAsync(third, new { toRoomId = await _client.CreateRoomAsync(token, propertyId, "C2"), monthlyRent = 4_000_000, rentFrom = "CurrentPeriod" });
        (await RentTermsAsync(third)).Should().Equal(($"{start:yyyy-MM-dd}", 4_000_000m));
    }

    /// <summary>
    /// CT-BR-48 ở khu thu trước: kỳ chuyển đã chốt tiền phòng mà áp giá mới cho kỳ đó ⇒ khoản chênh lệch cả kỳ, giá mới từ kỳ sau.
    /// CT-BR-14: khoản chênh lệch đã đánh dấu đã thu ⇒ chuyển lại trong ngày bị chặn; bỏ đánh dấu ⇒ chuyển lại được, khoản bị hủy, giá trả lại.
    /// </summary>
    [Fact]
    public async Task Transfer_WhenPeriodRentAlreadyBilled_CreatesDifferenceCharge_SettledChargeBlocksSameDayUndo()
    {
        var owner = await _client.CreateActiveOwnerAsync();
        var token = owner.Tokens.AccessToken;
        var today = TestData.Today(factory);
        var month = new DateOnly(today.Year, today.Month, 1);
        var propertyId = await _client.CreatePropertyAsync(token, anchorDay: 1, chargeMode: "Prepaid");
        var roomA = await _client.CreateRoomAsync(token, propertyId, "A1");
        var roomB = await _client.CreateRoomAsync(token, propertyId, "A2");
        var contractId = await _client.CreateContractAsync(token, roomA, await _client.CreateRenterAsync(token), month.AddMonths(-1));
        (await _client.PostJsonAsync($"/api/v1/contracts/{contractId}/activate", null, token)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        foreach (var billingMonth in new[] { month.AddMonths(-1), month })
        {
            var invoice = await InvoiceAsync(propertyId, contractId, roomA, billingMonth, token);
            var finalized = await _client.PostJsonAsync($"/api/v1/invoices/{invoice.GetProperty("summary").GetProperty("id").GetGuid()}/finalize", null, token);
            finalized.StatusCode.Should().Be(HttpStatusCode.OK, await finalized.Content.ReadAsStringAsync());
        }

        var moved = await _client.PostJsonAsync($"/api/v1/contracts/{contractId}/transfer-room",
            new { toRoomId = roomB, monthlyRent = 4_000_000, rentFrom = "CurrentPeriod" }, token);
        moved.StatusCode.Should().Be(HttpStatusCode.OK, await moved.Content.ReadAsStringAsync());

        var charge = (await (await _client.GetAsync($"/api/v1/room-charges?roomId={roomB}", token)).ReadAsync<List<JsonElement>>()).Single();
        charge.GetProperty("kind").GetString().Should().Be("Surcharge");
        charge.GetProperty("amount").GetDecimal().Should().Be(500_000, "4,0tr − 3,5tr cho cả kỳ, không chia ngày");
        (await DetailAsync(contractId, token)).GetProperty("rentTerms").EnumerateArray()
            .Should().Contain(t => t.GetProperty("effectiveFrom").GetString() == $"{month.AddMonths(1):yyyy-MM-dd}" && t.GetProperty("monthlyRent").GetDecimal() == 4_000_000);

        var chargeId = charge.GetProperty("id").GetGuid();
        (await _client.PostJsonAsync($"/api/v1/room-charges/{chargeId}/settle", new { settledOn = today, method = "Cash" }, token))
            .IsSuccessStatusCode.Should().BeTrue();
        Task<HttpResponseMessage> BackAsync() => _client.PostJsonAsync($"/api/v1/contracts/{contractId}/transfer-room", new { toRoomId = roomA }, token);
        (await (await BackAsync()).ReadProblemCodeAsync()).Should().Be("ROOM_TRANSFER_CANCEL_SETTLED", "người thuê đã trả khoản chênh lệch");

        (await _client.DeleteAsync($"/api/v1/room-charges/{chargeId}/settle", token)).IsSuccessStatusCode.Should().BeTrue();
        (await BackAsync()).StatusCode.Should().Be(HttpStatusCode.OK);
        (await (await _client.GetAsync($"/api/v1/room-charges?roomId={roomB}", token)).ReadAsync<List<JsonElement>>())
            .Single().GetProperty("status").GetString().Should().Be("Cancelled");
        (await DetailAsync(contractId, token)).GetProperty("rentTerms").EnumerateArray()
            .Should().NotContain(t => t.GetProperty("monthlyRent").GetDecimal() == 4_000_000, "giá thuê trả lại như trước lần chuyển");
    }

    /// <summary>CT-BR-11 (17/10/2026): đã chuyển phòng thì không trả phòng trước ngày vào phòng hiện tại (trước đây lỗi 500).</summary>
    [Fact]
    public async Task Liquidation_BeforeRoomTransferDate_IsRefused()
    {
        var owner = await _client.CreateActiveOwnerAsync();
        var token = owner.Tokens.AccessToken;
        var today = TestData.Today(factory);
        var propertyId = await _client.CreatePropertyAsync(token, anchorDay: 1);
        var roomA = await _client.CreateRoomAsync(token, propertyId, "A1");
        var roomB = await _client.CreateRoomAsync(token, propertyId, "A2");
        var contractId = await _client.CreateContractAsync(token, roomA, await _client.CreateRenterAsync(token), today.AddDays(-20));
        (await _client.PostJsonAsync($"/api/v1/contracts/{contractId}/activate", null, token)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await _client.PostJsonAsync($"/api/v1/contracts/{contractId}/transfer-room", new { toRoomId = roomB }, token)).StatusCode.Should().Be(HttpStatusCode.OK);

        var refused = await _client.PostJsonAsync($"/api/v1/contracts/{contractId}/liquidation/start",
            new { actualEndDate = today.AddDays(-3), reason = "Abandoned", note = "Phát hiện bỏ đi từ 3 ngày trước, đồ để lại 1 vali" }, token);
        refused.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await refused.ReadProblemCodeAsync()).Should().Be("END_DATE_BEFORE_ROOM_TRANSFER");
        (await _client.PostJsonAsync($"/api/v1/contracts/{contractId}/liquidation/start",
            new { actualEndDate = today, reason = "Abandoned", note = "Phát hiện bỏ đi hôm nay" }, token)).StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    /// <summary>M02 PR-BR-16: nợ đi theo HĐ — chuyển phòng xong phòng cũ hết nhãn "Còn nợ", phòng mới mang nợ.</summary>
    [Fact]
    public async Task Transfer_MovesOutstandingDebtLabel_FromOldRoomToNewRoom()
    {
        var owner = await _client.CreateActiveOwnerAsync();
        var token = owner.Tokens.AccessToken;
        var today = TestData.Today(factory);
        var month = new DateOnly(today.Year, today.Month, 1).AddMonths(-1);
        var propertyId = await _client.CreatePropertyAsync(token, anchorDay: 1, chargeMode: "Postpaid");
        var roomA = await _client.CreateRoomAsync(token, propertyId, "A1");
        var roomB = await _client.CreateRoomAsync(token, propertyId, "A2");
        var contractId = await _client.CreateContractAsync(token, roomA, await _client.CreateRenterAsync(token), month);
        (await _client.PostJsonAsync($"/api/v1/contracts/{contractId}/activate", null, token)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        var invoice = await InvoiceAsync(propertyId, contractId, roomA, month, token);
        var finalized = await _client.PostJsonAsync($"/api/v1/invoices/{invoice.GetProperty("summary").GetProperty("id").GetGuid()}/finalize", null, token);
        finalized.StatusCode.Should().Be(HttpStatusCode.OK, await finalized.Content.ReadAsStringAsync());

        async Task<decimal> OutstandingAsync(Guid roomId) =>
            (await (await _client.GetAsync($"/api/v1/rooms/{roomId}", token)).ReadAsync<JsonElement>()).GetProperty("outstandingAmount").GetDecimal();
        var debt = await OutstandingAsync(roomA);
        debt.Should().BeGreaterThan(0);

        var moved = await _client.PostJsonAsync($"/api/v1/contracts/{contractId}/transfer-room", new { toRoomId = roomB }, token);
        moved.StatusCode.Should().Be(HttpStatusCode.OK, await moved.Content.ReadAsStringAsync());

        (await OutstandingAsync(roomA)).Should().Be(0, "nợ đi theo HĐ, phòng cũ hết nhãn");
        (await OutstandingAsync(roomB)).Should().Be(debt);
    }

    /// <summary>M02 PR-BR-02: số phòng đang thuê của khu khớp trạng thái phòng — ngày chuyển tính cả phòng cũ lẫn phòng mới.</summary>
    [Fact]
    public async Task Transfer_OnTransferDay_PropertyCountsBothRoomsAsOccupied()
    {
        var owner = await _client.CreateActiveOwnerAsync();
        var token = owner.Tokens.AccessToken;
        var today = TestData.Today(factory);
        var propertyId = await _client.CreatePropertyAsync(token, anchorDay: 1);
        var roomA = await _client.CreateRoomAsync(token, propertyId, "A1");
        var roomB = await _client.CreateRoomAsync(token, propertyId, "A2");
        var contractId = await _client.CreateContractAsync(token, roomA, await _client.CreateRenterAsync(token), today.AddDays(-10));
        (await _client.PostJsonAsync($"/api/v1/contracts/{contractId}/activate", null, token)).StatusCode.Should().Be(HttpStatusCode.NoContent);

        async Task<int> OccupiedAsync() =>
            (await (await _client.GetAsync("/api/v1/properties?pageSize=100", token)).ReadAsync<JsonElement>()).GetProperty("items").EnumerateArray()
            .Single(p => p.GetProperty("id").GetGuid() == propertyId).GetProperty("occupiedRoomCount").GetInt32();
        (await OccupiedAsync()).Should().Be(1);

        var moved = await _client.PostJsonAsync($"/api/v1/contracts/{contractId}/transfer-room", new { toRoomId = roomB }, token);
        moved.StatusCode.Should().Be(HttpStatusCode.OK, await moved.Content.ReadAsStringAsync());

        (await RoomStatusAsync(roomA, token)).Should().Be("Occupied");
        (await OccupiedAsync()).Should().Be(2, "ngày chuyển phòng tính là đang thuê ở cả 2 phòng");
    }
}
