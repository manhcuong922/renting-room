using System.Net;
using System.Text.Json;
using renting_room.IntegrationTests.Infrastructure;

namespace renting_room.IntegrationTests;

/// <summary>M05 CT-UC-12 / CT-BR-14, 47: chuyển phòng cùng khu — HĐ giữ nguyên, điện nước phòng cũ chốt thành khoản phát sinh tại ngày chuyển.</summary>
[Collection(ApiCollection.Name)]
public sealed class RoomTransferTests(ApiFactory factory)
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task TransferMidMonth_KeepsContract_RoomStatusesSwap_OldRoomUsageBecomesCharge_BlocksInvalidTargets()
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

        // CT-BR-47: điện phòng cũ (130 − 100) × 3.500 chốt ngay thành khoản phát sinh hệ thống của HĐ — không sửa / hủy tay được.
        var charge = (await (await _client.GetAsync($"/api/v1/room-charges?roomId={roomA}", token)).ReadAsync<List<JsonElement>>()).Single();
        charge.GetProperty("amount").GetDecimal().Should().Be(105_000);
        charge.GetProperty("incurredOn").GetString().Should().Be($"{date:yyyy-MM-dd}");
        charge.GetProperty("status").GetString().Should().Be("Pending");
        (await (await _client.PostJsonAsync($"/api/v1/room-charges/{charge.GetProperty("id").GetGuid()}/cancel", new { reason = "Nhầm" }, token))
            .ReadProblemCodeAsync()).Should().Be("ROOM_CHARGE_SYSTEM");
        var closing = (await (await _client.GetAsync($"/api/v1/meters/{meterA}/readings", token)).ReadAsync<List<JsonElement>>())
            .First(r => r.GetProperty("kind").GetString() == "Final");
        (await (await _client.PutJsonAsync($"/api/v1/meter-readings/{closing.GetProperty("id").GetGuid()}", new { value = 125 }, token))
            .ReadProblemCodeAsync()).Should().Be("READING_LOCKED", "chỉ số của khoản chốt bị khóa");

        // Cuối tháng: phiếu chỉ tính công tơ phòng mới (520 − 500 = 20 kWh) + dòng phụ thu điện phòng cũ 30 kWh.
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
        metered.GetProperty("quantity").GetDecimal().Should().Be(20);
        metered.GetProperty("segments").GetArrayLength().Should().Be(1);
        var surcharge = invoice.GetProperty("lines").EnumerateArray().Single(l => l.GetProperty("type").GetString() == "Surcharge");
        surcharge.GetProperty("amount").GetDecimal().Should().Be(105_000);
        surcharge.GetProperty("quantity").GetDecimal().Should().Be(30);
        surcharge.GetProperty("feeTypeId").GetGuid().Should().Be(electricity);
        invoice.GetProperty("lines").EnumerateArray().Single(l => l.GetProperty("type").GetString() == "Rent").GetProperty("amount").GetDecimal()
            .Should().Be(3_200_000, "giá mới áp từ kỳ chưa chốt đầu tiên — lúc chuyển kỳ này chưa có phiếu");

        // Đã lập phiếu điện nước tới cuối tháng ⇒ không chuyển tiếp được với ngày trong tháng đó.
        (await (await _client.PostJsonAsync($"/api/v1/contracts/{contractId}/transfer-room", new
        {
            toRoomId = roomA, date = month.AddDays(20), oldRoomReadings = new[] { new { meterId = meterB, value = (decimal?)510 } }
        }, token)).ReadProblemCodeAsync()).Should().Be("ROOM_TRANSFER_ALREADY_BILLED");
    }

    /// <summary>CT-BR-14 + MT-BR-04, 12: chuyển đi rồi chuyển về phòng cũ — mỗi lần chốt 1 khoản; phiếu không tính phần phòng trống.</summary>
    [Fact]
    public async Task TransferAwayAndBack_ToSameRoom_Works_AndInvoiceStartsFromLatestHandover()
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
        var contractId = await _client.CreateContractAsync(token, roomA, await _client.CreateRenterAsync(token), month);
        (await _client.PostJsonAsync($"/api/v1/contracts/{contractId}/activate",
            new { handoverReadings = new[] { new { meterId = meterA, value = (decimal?)null } } }, token)).StatusCode.Should().Be(HttpStatusCode.NoContent);

        Task<HttpResponseMessage> TransferAsync(Guid to, DateOnly on, Guid oldMeter, decimal final, Guid newMeter, decimal? handover) =>
            _client.PostJsonAsync($"/api/v1/contracts/{contractId}/transfer-room", new
            {
                toRoomId = to, date = on, oldRoomReadings = new[] { new { meterId = oldMeter, value = (decimal?)final } },
                newRoomReadings = new[] { new { meterId = newMeter, value = handover } }
            }, token);
        (await TransferAsync(roomB, month.AddDays(10), meterA, 130, meterB, null)).StatusCode.Should().Be(HttpStatusCode.OK);
        // Quay về A ngày 21: B dùng 515 − 500 = 15 kWh; A lúc trống thợ dùng 8 kWh ⇒ nhận phòng lại ở 138.
        var back = await TransferAsync(roomA, month.AddDays(20), meterB, 515, meterA, 138);
        back.StatusCode.Should().Be(HttpStatusCode.OK, await back.Content.ReadAsStringAsync());

        var charges = await (await _client.GetAsync($"/api/v1/room-charges?propertyId={propertyId}", token)).ReadAsync<List<JsonElement>>();
        charges.Select(c => c.GetProperty("amount").GetDecimal()).Should().BeEquivalentTo([105_000m, 52_500m]);

        (await _client.PutJsonAsync($"/api/v1/properties/{propertyId}/meter-readings", new
        {
            readings = new[] { new { meterId = meterA, contractId, closingPeriodStart = month, readingDate = month.AddMonths(1), value = 160m } }
        }, token)).StatusCode.Should().Be(HttpStatusCode.OK);
        await _client.PostJsonAsync("/api/v1/invoices/generate", new { propertyId, billingMonth = $"{month:yyyy-MM}", roomIds = new[] { roomA } }, token);
        var page = await (await _client.GetAsync($"/api/v1/invoices?contractId={contractId}", token)).ReadAsync<JsonElement>();
        var invoice = await (await _client.GetAsync($"/api/v1/invoices/{page.GetProperty("items")[0].GetProperty("id").GetGuid()}", token))
            .ReadAsync<JsonElement>();
        var metered = invoice.GetProperty("lines").EnumerateArray().Single(l => l.GetProperty("type").GetString() == "Metered");
        metered.GetProperty("quantity").GetDecimal().Should().Be(22, "160 − 138: không tính 8 kWh lúc phòng trống, không nối từ chỉ số cuối lần ở trước");
        invoice.GetProperty("lines").EnumerateArray().Where(l => l.GetProperty("type").GetString() == "Surcharge")
            .Sum(l => l.GetProperty("amount").GetDecimal()).Should().Be(157_500);
    }

    /// <summary>
    /// CT-BR-47 ở khu thu trước (điện nước kỳ trước nằm trên phiếu kỳ này): kỳ trước đã ghi chỉ số cuối kỳ ⇒ phiếu vẫn tính kỳ đó ở phòng cũ,
    /// khoản chốt chỉ gồm phần sau; chưa ghi ⇒ khoản chốt gồm cả kỳ trước, phiếu không đòi chỉ số phòng cũ, lưới không hỏi công tơ phòng mới.
    /// </summary>
    [Fact]
    public async Task Transfer_PrepaidProperty_ClosingChargeCoversWhatInvoicesDoNot()
    {
        var owner = await _client.CreateActiveOwnerAsync();
        var token = owner.Tokens.AccessToken;
        var today = TestData.Today(factory);
        var month = new DateOnly(today.Year, today.Month, 1);
        var previous = month.AddMonths(-1);
        var propertyId = await _client.CreatePropertyAsync(token, anchorDay: 1, chargeMode: "Prepaid");
        var electricity = await _client.FeeIdAsync(token, propertyId, "Điện");
        await _client.PostJsonAsync($"/api/v1/fee-types/{electricity}/prices", new { effectiveFrom = previous.AddDays(-30), unitPrice = 3500 }, token);

        async Task<(Guid Contract, Guid OldMeter, Guid NewRoom)> TenancyAsync(string oldCode, string newCode)
        {
            var oldRoom = await _client.CreateRoomAsync(token, propertyId, oldCode);
            var newRoom = await _client.CreateRoomAsync(token, propertyId, newCode);
            var oldMeter = await _client.InstallMeterAsync(token, oldRoom, electricity, previous.AddDays(-1), 100);
            await _client.InstallMeterAsync(token, newRoom, electricity, previous.AddDays(-1), 500);
            var contract = await _client.CreateContractAsync(token, oldRoom, await _client.CreateRenterAsync(token), previous);
            (await _client.PostJsonAsync($"/api/v1/contracts/{contract}/activate",
                new { handoverReadings = new[] { new { meterId = oldMeter, value = (decimal?)null } } }, token)).StatusCode.Should().Be(HttpStatusCode.NoContent);
            return (contract, oldMeter, newRoom);
        }
        async Task TransferAsync(Guid contract, Guid oldMeter, Guid newRoom, decimal final)
        {
            var moved = await _client.PostJsonAsync($"/api/v1/contracts/{contract}/transfer-room",
                new { toRoomId = newRoom, date = today, oldRoomReadings = new[] { new { meterId = oldMeter, value = (decimal?)final } } }, token);
            moved.StatusCode.Should().Be(HttpStatusCode.OK, await moved.Content.ReadAsStringAsync());
        }
        async Task<JsonElement> InvoiceAsync(Guid contract, Guid room, DateOnly billingMonth)
        {
            await _client.PostJsonAsync("/api/v1/invoices/generate", new { propertyId, billingMonth = $"{billingMonth:yyyy-MM}", roomIds = new[] { room } }, token);
            var page = await (await _client.GetAsync($"/api/v1/invoices?contractId={contract}&billingMonth={billingMonth:yyyy-MM}", token)).ReadAsync<JsonElement>();
            return await (await _client.GetAsync($"/api/v1/invoices/{page.GetProperty("items")[0].GetProperty("id").GetGuid()}", token)).ReadAsync<JsonElement>();
        }
        static decimal Sum(JsonElement invoice, string type, string property) => invoice.GetProperty("lines").EnumerateArray()
            .Where(l => l.GetProperty("type").GetString() == type).Sum(l => l.GetProperty(property).GetDecimal());

        // (1) Đã ghi chỉ số cuối kỳ trước (150) rồi mới chuyển ở 165 ⇒ khoản chốt 15 kWh; phiếu kỳ này vẫn tính 50 kWh kỳ trước ở phòng cũ.
        var recorded = await TenancyAsync("A1", "A2");
        (await _client.PutJsonAsync($"/api/v1/properties/{propertyId}/meter-readings", new
        {
            readings = new[] { new { meterId = recorded.OldMeter, contractId = recorded.Contract, closingPeriodStart = previous, readingDate = month, value = 150m } }
        }, token)).StatusCode.Should().Be(HttpStatusCode.OK);
        await TransferAsync(recorded.Contract, recorded.OldMeter, recorded.NewRoom, 165);
        await InvoiceAsync(recorded.Contract, recorded.NewRoom, previous);
        var first = await InvoiceAsync(recorded.Contract, recorded.NewRoom, month);
        Sum(first, "Metered", "quantity").Should().Be(50, "100 → 150 của kỳ trước, tính trên phiếu kỳ này (thu trước)");
        Sum(first, "Surcharge", "amount").Should().Be(52_500, "15 kWh sau chỉ số cuối kỳ tới ngày chuyển");

        // (2) Chưa ghi chỉ số cuối kỳ trước, chuyển ở 180 ⇒ khoản chốt 80 kWh gồm cả kỳ trước; phiếu không có dòng điện và không báo thiếu chỉ số.
        var unrecorded = await TenancyAsync("B1", "B2");
        await TransferAsync(unrecorded.Contract, unrecorded.OldMeter, unrecorded.NewRoom, 180);
        var sheet = await (await _client.GetAsync($"/api/v1/properties/{propertyId}/meter-reading-sheet?billingMonth={month:yyyy-MM}", token))
            .ReadAsync<JsonElement>();
        sheet.GetProperty("rows").EnumerateArray().Should().NotContain(r => r.GetProperty("contractId").GetGuid() == unrecorded.Contract,
            "kỳ trước HĐ chưa ở phòng mới, phần phòng cũ đã nằm trong khoản chốt");
        await InvoiceAsync(unrecorded.Contract, unrecorded.NewRoom, previous);
        var second = await InvoiceAsync(unrecorded.Contract, unrecorded.NewRoom, month);
        second.GetProperty("lines").EnumerateArray().Should().NotContain(l => l.GetProperty("type").GetString() == "Metered");
        second.GetProperty("issues").EnumerateArray().Select(i => i.GetProperty("code").GetString()).Should().NotContain("MISSING_READING");
        Sum(second, "Surcharge", "amount").Should().Be(280_000);
    }

    /// <summary>CT-BR-48: giá mới áp theo quy tắc nửa kỳ (không chia ngày), bỏ dịch vụ không dùng ở phòng mới, cảnh báo công tơ / giá niêm yết.</summary>
    [Fact]
    public async Task Transfer_RentFollowsHalfPeriodRule_ServicesCanBeDropped_WarnsOnMeterAndListedRent()
    {
        var owner = await _client.CreateActiveOwnerAsync();
        var token = owner.Tokens.AccessToken;
        var today = TestData.Today(factory);
        var month = new DateOnly(today.Year, today.Month, 1).AddMonths(-1);
        var propertyId = await _client.CreatePropertyAsync(token, anchorDay: 1, chargeMode: "Postpaid");
        var electricity = await _client.FeeIdAsync(token, propertyId, "Điện");
        await _client.PostJsonAsync($"/api/v1/fee-types/{electricity}/prices", new { effectiveFrom = month.AddDays(-30), unitPrice = 3500 }, token);
        var airCon = await (await _client.PostJsonAsync($"/api/v1/properties/{propertyId}/fee-types", new
        {
            name = "Phí điều hòa", group = "Service", chargeBasis = "PerRoom", unit = "phòng", autoAttach = false,
            initialPrice = new { effectiveFrom = month.AddDays(-30), unitPrice = 100_000 }
        }, token)).ReadIdAsync();

        async Task<Guid> ContractInAsync(Guid room)
        {
            var id = await _client.CreateContractAsync(token, room, await _client.CreateRenterAsync(token), month); // giá thuê 3,5tr
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
            (await (await _client.GetAsync($"/api/v1/contracts/{contract}", token)).ReadAsync<JsonElement>()).GetProperty("rentTerms").EnumerateArray()
            .Select(t => (t.GetProperty("effectiveFrom").GetString()!, t.GetProperty("monthlyRent").GetDecimal())).OrderBy(t => t.Item1).ToList();
        var lateInPeriod = month.AddDays(20); // đã ở phòng cũ 20 ngày — quá nửa kỳ

        // (1) Không gửi giá: giữ giá cũ nhưng cảnh báo vì giá niêm yết phòng mới khác; bỏ phí điều hòa; phòng mới không có công tơ điện.
        var roomA = await _client.CreateRoomAsync(token, propertyId, "A1");
        var meterA = await _client.InstallMeterAsync(token, roomA, electricity, month.AddDays(-1), 100);
        var first = await _client.CreateContractAsync(token, roomA, await _client.CreateRenterAsync(token), month);
        (await _client.PostJsonAsync($"/api/v1/contracts/{first}/activate",
            new { handoverReadings = new[] { new { meterId = meterA, value = (decimal?)null } } }, token)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await _client.PutJsonAsync($"/api/v1/contracts/{first}/fees/{airCon}", new { quantity = 1 }, token)).IsSuccessStatusCode.Should().BeTrue();
        var kept = await TransferAsync(first, new
        {
            toRoomId = await _client.CreateRoomAsync(token, propertyId, "A2", listedRent: 4_000_000), date = lateInPeriod,
            oldRoomReadings = new[] { new { meterId = meterA, value = (decimal?)130 } }, removeFeeTypeIds = new[] { airCon }
        });
        kept.GetProperty("warnings").EnumerateArray().Select(w => w.GetProperty("code").GetString())
            .Should().Contain(["RESIDENCE_ROOM_CHANGED", "ROOM_TRANSFER_RENT_UNCHANGED", "METER_MISSING_IN_NEW_ROOM"]);
        (await RentTermsAsync(first)).Should().Equal(($"{month:yyyy-MM-dd}", 3_500_000m));
        (await (await _client.GetAsync($"/api/v1/contracts/{first}", token)).ReadAsync<JsonElement>()).GetProperty("fees").EnumerateArray()
            .Should().NotContain(f => f.GetProperty("feeTypeId").GetGuid() == airCon, "dịch vụ theo phòng không mang sang phòng mới");

        // (2) Gửi giá mới, quá nửa kỳ ⇒ kỳ này vẫn giá cũ, giá mới từ kỳ sau.
        var second = await ContractInAsync(await _client.CreateRoomAsync(token, propertyId, "B1"));
        await TransferAsync(second, new { toRoomId = await _client.CreateRoomAsync(token, propertyId, "B2"), date = lateInPeriod, monthlyRent = 4_000_000 });
        (await RentTermsAsync(second)).Should().Equal(($"{month:yyyy-MM-dd}", 3_500_000m), ($"{month.AddMonths(1):yyyy-MM-dd}", 4_000_000m));

        // (3) Chủ trọ chọn áp ngay kỳ này dù đã quá nửa kỳ ⇒ cả kỳ giá mới.
        var third = await ContractInAsync(await _client.CreateRoomAsync(token, propertyId, "C1"));
        await TransferAsync(third, new
        {
            toRoomId = await _client.CreateRoomAsync(token, propertyId, "C2"), date = lateInPeriod, monthlyRent = 4_000_000, rentFrom = "CurrentPeriod"
        });
        (await RentTermsAsync(third)).Should().Equal(($"{month:yyyy-MM-dd}", 4_000_000m));
    }

    /// <summary>CT-BR-48 ở khu thu trước: kỳ chuyển đã chốt tiền phòng mà áp giá mới cho kỳ đó ⇒ khoản phát sinh chênh lệch cả kỳ, giá mới từ kỳ sau.</summary>
    [Fact]
    public async Task Transfer_WhenPeriodRentAlreadyBilled_CreatesWholePeriodDifferenceCharge()
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
            await _client.PostJsonAsync("/api/v1/invoices/generate", new { propertyId, billingMonth = $"{billingMonth:yyyy-MM}", roomIds = new[] { roomA } }, token);
            var page = await (await _client.GetAsync($"/api/v1/invoices?contractId={contractId}&billingMonth={billingMonth:yyyy-MM}", token)).ReadAsync<JsonElement>();
            var finalized = await _client.PostJsonAsync($"/api/v1/invoices/{page.GetProperty("items")[0].GetProperty("id").GetGuid()}/finalize", null, token);
            finalized.StatusCode.Should().Be(HttpStatusCode.OK, await finalized.Content.ReadAsStringAsync());
        }

        var moved = await _client.PostJsonAsync($"/api/v1/contracts/{contractId}/transfer-room",
            new { toRoomId = roomB, date = today, monthlyRent = 4_000_000, rentFrom = "CurrentPeriod" }, token);
        moved.StatusCode.Should().Be(HttpStatusCode.OK, await moved.Content.ReadAsStringAsync());

        var charge = (await (await _client.GetAsync($"/api/v1/room-charges?roomId={roomB}", token)).ReadAsync<List<JsonElement>>()).Single();
        charge.GetProperty("kind").GetString().Should().Be("Surcharge");
        charge.GetProperty("amount").GetDecimal().Should().Be(500_000, "4,0tr − 3,5tr cho cả kỳ, không chia ngày");
        var terms = (await (await _client.GetAsync($"/api/v1/contracts/{contractId}", token)).ReadAsync<JsonElement>()).GetProperty("rentTerms");
        terms.EnumerateArray().Should().Contain(t => t.GetProperty("effectiveFrom").GetString() == $"{month.AddMonths(1):yyyy-MM-dd}"
            && t.GetProperty("monthlyRent").GetDecimal() == 4_000_000);
    }

    /// <summary>CT-BR-14: hủy lần chuyển gần nhất — trả lại phòng, hủy khoản chốt và chỉ số; chuyển lại được với số khác.</summary>
    [Fact]
    public async Task CancelTransfer_RestoresRoom_CancelsClosingCharge_AllowsTransferAgain()
    {
        var owner = await _client.CreateActiveOwnerAsync();
        var token = owner.Tokens.AccessToken;
        var today = TestData.Today(factory);
        var start = today.AddDays(-20);
        var propertyId = await _client.CreatePropertyAsync(token, anchorDay: 1, chargeMode: "Postpaid");
        var electricity = await _client.FeeIdAsync(token, propertyId, "Điện");
        await _client.PostJsonAsync($"/api/v1/fee-types/{electricity}/prices", new { effectiveFrom = start.AddDays(-30), unitPrice = 3500 }, token);
        var roomA = await _client.CreateRoomAsync(token, propertyId, "A1");
        var roomB = await _client.CreateRoomAsync(token, propertyId, "A2");
        var meterA = await _client.InstallMeterAsync(token, roomA, electricity, start.AddDays(-1), 100);
        var contractId = await _client.CreateContractAsync(token, roomA, await _client.CreateRenterAsync(token), start);
        (await _client.PostJsonAsync($"/api/v1/contracts/{contractId}/activate",
            new { handoverReadings = new[] { new { meterId = meterA, value = (decimal?)null } } }, token)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        var cancelUrl = $"/api/v1/contracts/{contractId}/transfer-room/cancel";
        (await (await _client.PostJsonAsync(cancelUrl, null, token)).ReadProblemCodeAsync()).Should().Be("ROOM_TRANSFER_NOTHING_TO_CANCEL");

        Task<HttpResponseMessage> TransferAsync(decimal final) => _client.PostJsonAsync($"/api/v1/contracts/{contractId}/transfer-room",
            new { toRoomId = roomB, date = today.AddDays(-3), oldRoomReadings = new[] { new { meterId = meterA, value = (decimal?)final } } }, token);
        (await TransferAsync(1300)).StatusCode.Should().Be(HttpStatusCode.OK); // gõ nhầm 130 thành 1300

        var cancelled = await _client.PostJsonAsync(cancelUrl, null, token);
        cancelled.StatusCode.Should().Be(HttpStatusCode.OK, await cancelled.Content.ReadAsStringAsync());
        (await cancelled.ReadAsync<JsonElement>()).GetProperty("warnings").EnumerateArray().Select(w => w.GetProperty("code").GetString())
            .Should().Contain("ROOM_TRANSFER_CANCELLED_CHECK_RENT");
        var detail = await (await _client.GetAsync($"/api/v1/contracts/{contractId}", token)).ReadAsync<JsonElement>();
        detail.GetProperty("roomId").GetGuid().Should().Be(roomA);
        detail.GetProperty("roomSince").GetString().Should().Be($"{start:yyyy-MM-dd}");
        detail.GetProperty("roomMoves").GetArrayLength().Should().Be(0);
        (await (await _client.GetAsync($"/api/v1/rooms/{roomB}", token)).ReadAsync<JsonElement>()).GetProperty("status").GetString().Should().Be("Vacant");
        var charges = await (await _client.GetAsync($"/api/v1/room-charges?roomId={roomA}", token)).ReadAsync<List<JsonElement>>();
        charges.Should().ContainSingle().Which.GetProperty("status").GetString().Should().Be("Cancelled");

        (await TransferAsync(130)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await (await _client.GetAsync($"/api/v1/room-charges?roomId={roomA}&status=Pending", token)).ReadAsync<List<JsonElement>>())
            .Should().ContainSingle().Which.GetProperty("amount").GetDecimal().Should().Be(105_000);
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
        await _client.PostJsonAsync("/api/v1/invoices/generate", new { propertyId, billingMonth = $"{month:yyyy-MM}", roomIds = new[] { roomA } }, token);
        var page = await (await _client.GetAsync($"/api/v1/invoices?contractId={contractId}", token)).ReadAsync<JsonElement>();
        var finalized = await _client.PostJsonAsync($"/api/v1/invoices/{page.GetProperty("items")[0].GetProperty("id").GetGuid()}/finalize", null, token);
        finalized.StatusCode.Should().Be(HttpStatusCode.OK, await finalized.Content.ReadAsStringAsync());

        async Task<decimal> OutstandingAsync(Guid roomId) =>
            (await (await _client.GetAsync($"/api/v1/rooms/{roomId}", token)).ReadAsync<JsonElement>()).GetProperty("outstandingAmount").GetDecimal();
        var debt = await OutstandingAsync(roomA);
        debt.Should().BeGreaterThan(0);

        var moved = await _client.PostJsonAsync($"/api/v1/contracts/{contractId}/transfer-room",
            new { toRoomId = roomB, date = today, oldRoomReadings = (object?)null, newRoomReadings = (object?)null }, token);
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

        var moved = await _client.PostJsonAsync($"/api/v1/contracts/{contractId}/transfer-room",
            new { toRoomId = roomB, date = today, oldRoomReadings = (object?)null, newRoomReadings = (object?)null }, token);
        moved.StatusCode.Should().Be(HttpStatusCode.OK, await moved.Content.ReadAsStringAsync());

        (await (await _client.GetAsync($"/api/v1/rooms/{roomA}", token)).ReadAsync<JsonElement>()).GetProperty("status").GetString().Should().Be("Occupied");
        (await OccupiedAsync()).Should().Be(2, "ngày chuyển phòng tính là đang thuê ở cả 2 phòng");
    }
}
