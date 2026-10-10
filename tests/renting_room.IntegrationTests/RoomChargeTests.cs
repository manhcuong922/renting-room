using System.Net;
using System.Text.Json;
using renting_room.IntegrationTests.Infrastructure;

namespace renting_room.IntegrationTests;

/// <summary>
/// M07 BL-BR-30..37 — khoản phát sinh theo phòng: phụ thu / bù tạo ngay lúc xảy ra, có trạng thái đã thanh toán, cuối tháng tự vào phiếu;
/// khoản đã thanh toán hiện trên phiếu nhưng không tính vào tổng (10 kịch bản trong plan M07 §12).
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class RoomChargeTests(ApiFactory factory)
{
    private const decimal Rent = 3_000_000;
    private readonly HttpClient _client = factory.CreateClient();

    private sealed record Setup(string Token, Guid PropertyId, Guid RoomId, Guid ContractId, DateOnly FirstMonth, DateOnly Today);

    /// <summary>Khu thu sau chốt ngày 1, không công tơ; HĐ 3tr bắt đầu từ đầu tháng cách đây <paramref name="monthsAgo"/> tháng.</summary>
    private async Task<Setup> ArrangeAsync(int monthsAgo = 1)
    {
        var owner = await _client.CreateActiveOwnerAsync();
        var token = owner.Tokens.AccessToken;
        var today = TestData.Today(factory);
        var start = new DateOnly(today.Year, today.Month, 1).AddMonths(-monthsAgo);
        var propertyId = await _client.CreatePropertyAsync(token, anchorDay: 1, chargeMode: "Postpaid");
        var roomId = await _client.CreateRoomAsync(token, propertyId);
        var renter = await _client.CreateRenterAsync(token);
        var contractId = await (await _client.PostJsonAsync("/api/v1/contracts", new
        {
            roomId, contract = new { representativeRenterId = renter, startDate = start, monthlyRent = Rent, depositAmount = 0, occupants = new[] { new { renterId = renter } } }
        }, token)).ReadIdAsync();
        (await _client.PostJsonAsync($"/api/v1/contracts/{contractId}/activate", null, token)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        return new Setup(token, propertyId, roomId, contractId, start, today);
    }

    private Task<HttpResponseMessage> CreateAsync(
        Setup s, string kind, decimal amount, DateOnly incurredOn, bool settled = false, Guid? roomId = null, string description = "Thay khóa cửa") =>
        _client.PostJsonAsync($"/api/v1/rooms/{roomId ?? s.RoomId}/charges", new
        {
            kind, description, amount, incurredOn, reason = "Phát sinh trong tháng", settled, settledOn = (DateOnly?)null, method = "Cash"
        }, s.Token);

    private async Task<JsonElement> CreatedAsync(Setup s, string kind, decimal amount, DateOnly incurredOn, bool settled = false)
    {
        var response = await CreateAsync(s, kind, amount, incurredOn, settled);
        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return await response.ReadAsync<JsonElement>();
    }

    private async Task<Guid> GenerateAsync(Setup s, DateOnly month)
    {
        await _client.PostJsonAsync("/api/v1/invoices/generate", new { propertyId = s.PropertyId, billingMonth = $"{month:yyyy-MM}" }, s.Token);
        var page = await (await _client.GetAsync($"/api/v1/invoices?contractId={s.ContractId}&billingMonth={month:yyyy-MM}", s.Token)).ReadAsync<JsonElement>();
        return page.GetProperty("items").EnumerateArray().First(i => i.GetProperty("status").GetString() != "Void").GetProperty("id").GetGuid();
    }

    private async Task<JsonElement> InvoiceAsync(Setup s, Guid id) =>
        await (await _client.GetAsync($"/api/v1/invoices/{id}", s.Token)).ReadAsync<JsonElement>();

    private static decimal Total(JsonElement invoice) => invoice.GetProperty("summary").GetProperty("totalAmount").GetDecimal();

    private static JsonElement? ChargeLine(JsonElement invoice, Guid chargeId)
    {
        foreach (var line in invoice.GetProperty("lines").EnumerateArray())
            if (line.GetProperty("roomChargeId").ValueKind != JsonValueKind.Null && line.GetProperty("roomChargeId").GetGuid() == chargeId)
                return line;
        return null;
    }

    private async Task<string?> StatusAsync(Setup s, Guid chargeId)
    {
        var list = await (await _client.GetAsync($"/api/v1/rooms/{s.RoomId}/charges", s.Token)).ReadAsync<JsonElement>();
        return list.EnumerateArray().Single(c => c.GetProperty("id").GetGuid() == chargeId).GetProperty("status").GetString();
    }

    [Fact]
    public async Task ChargeWaitsForDraft_ThenOnInvoice_SettledShownButNotCounted_MarkUnmarkCancel()
    {
        var s = await ArrangeAsync();
        var month = s.FirstMonth;
        var emptyRoom = await _client.CreateRoomAsync(s.Token, s.PropertyId);
        (await (await CreateAsync(s, "Surcharge", 250_000, month.AddDays(2), roomId: emptyRoom)).ReadProblemCodeAsync())
            .Should().Be("ROOM_CHARGE_NO_TENANT", "phòng trống — chỉ liên quan chủ trọ");

        // 1. Chưa có nháp ⇒ chờ; lập nháp ⇒ có dòng phụ thu, tổng tăng.
        var lockFee = (await CreatedAsync(s, "Surcharge", 250_000, month.AddDays(2))).GetProperty("id").GetGuid();
        (await StatusAsync(s, lockFee)).Should().Be("Pending");
        var invoiceId = await GenerateAsync(s, month);
        var invoice = await InvoiceAsync(s, invoiceId);
        Total(invoice).Should().Be(Rent + 250_000);
        ChargeLine(invoice, lockFee)!.Value.GetProperty("type").GetString().Should().Be("Surcharge");
        (await StatusAsync(s, lockFee)).Should().Be("OnDraft");

        // 2. Nháp đã có ⇒ khoản bù gắn ngay, tổng giảm.
        var credit = (await CreatedAsync(s, "Credit", 50_000, month.AddDays(9))).GetProperty("id").GetGuid();
        invoice = await InvoiceAsync(s, invoiceId);
        Total(invoice).Should().Be(Rent + 200_000);
        ChargeLine(invoice, credit)!.Value.GetProperty("amount").GetDecimal().Should().Be(-50_000);

        // 3. Đã thanh toán ⇒ hiện trên phiếu, không tính tổng; đánh dấu / bỏ đánh dấu khoản đang trên nháp ⇒ tổng đổi theo.
        var paidNow = (await CreatedAsync(s, "Surcharge", 100_000, month.AddDays(12), settled: true)).GetProperty("id").GetGuid();
        invoice = await InvoiceAsync(s, invoiceId);
        ChargeLine(invoice, paidNow)!.Value.GetProperty("isSettled").GetBoolean().Should().BeTrue();
        Total(invoice).Should().Be(Rent + 200_000, "đã thu ngay ⇒ không thu lần 2");
        (await _client.PostJsonAsync($"/api/v1/room-charges/{lockFee}/settle", new { settledOn = s.Today, method = "BankTransfer" }, s.Token))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        Total(await InvoiceAsync(s, invoiceId)).Should().Be(Rent - 50_000);
        (await _client.DeleteAsync($"/api/v1/room-charges/{lockFee}/settle", s.Token)).StatusCode.Should().Be(HttpStatusCode.OK);
        Total(await InvoiceAsync(s, invoiceId)).Should().Be(Rent + 200_000);

        // 6. Hủy khoản đang trên nháp ⇒ dòng mất; xóa dòng trên nháp ⇒ khoản thành đã hủy.
        (await _client.PostJsonAsync($"/api/v1/room-charges/{credit}/cancel", new { reason = "Ghi nhầm phòng" }, s.Token))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        invoice = await InvoiceAsync(s, invoiceId);
        ChargeLine(invoice, credit).Should().BeNull();
        Total(invoice).Should().Be(Rent + 250_000);
        var paidLine = ChargeLine(invoice, paidNow)!.Value.GetProperty("id").GetGuid();
        (await _client.DeleteAsync($"/api/v1/invoices/{invoiceId}/lines/{paidLine}", s.Token)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await StatusAsync(s, paidNow)).Should().Be("Cancelled");
        (await StatusAsync(s, credit)).Should().Be("Cancelled");
    }

    [Fact]
    public async Task ChargeInFinalizedPeriod_RollsToNextInvoice_LockedWhenFinalized_ReturnsToPendingOnVoidOrDelete()
    {
        var s = await ArrangeAsync(monthsAgo: 2);
        var month1 = s.FirstMonth;
        var month2 = month1.AddMonths(1);
        var first = await GenerateAsync(s, month1);
        (await _client.PostJsonAsync($"/api/v1/invoices/{first}/finalize", null, s.Token)).StatusCode.Should().Be(HttpStatusCode.OK);

        // 4. Phát sinh ngày 29 của tháng đã chốt ⇒ sang phiếu tháng sau; phát sinh tháng này ⇒ không vào nháp tháng trước.
        var late = (await CreatedAsync(s, "Surcharge", 150_000, month1.AddDays(28))).GetProperty("id").GetGuid();
        (await StatusAsync(s, late)).Should().Be("Pending");
        ChargeLine(await InvoiceAsync(s, first), late).Should().BeNull("phiếu tháng đó đã chốt");
        var second = await GenerateAsync(s, month2);
        ChargeLine(await InvoiceAsync(s, second), late).Should().NotBeNull("đẩy sang phiếu kỳ kế tiếp");
        var thisMonth = (await CreatedAsync(s, "Surcharge", 80_000, s.Today)).GetProperty("id").GetGuid();
        ChargeLine(await InvoiceAsync(s, second), thisMonth).Should().BeNull("tháng nào ra tháng đấy");
        (await StatusAsync(s, thisMonth)).Should().Be("Pending");

        // 5. Chốt ⇒ khóa; hủy phiếu ⇒ về chờ, lập lại thì có lại; xóa nháp ⇒ về chờ.
        (await _client.PostJsonAsync($"/api/v1/invoices/{second}/finalize", null, s.Token)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await StatusAsync(s, late)).Should().Be("Billed");
        (await (await _client.PostJsonAsync($"/api/v1/room-charges/{late}/settle", new { settledOn = s.Today, method = "Cash" }, s.Token))
            .ReadProblemCodeAsync()).Should().Be("ROOM_CHARGE_LOCKED");
        (await (await _client.PostJsonAsync($"/api/v1/room-charges/{late}/cancel", new { reason = "Thử" }, s.Token)).ReadProblemCodeAsync())
            .Should().Be("ROOM_CHARGE_LOCKED");

        (await _client.PostJsonAsync($"/api/v1/invoices/{second}/void", new { reason = "Lập lại" }, s.Token)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await StatusAsync(s, late)).Should().Be("Pending");
        var again = await GenerateAsync(s, month2);
        again.Should().NotBe(second);
        var redo = await InvoiceAsync(s, again);
        ChargeLine(redo, late).Should().NotBeNull();
        Total(redo).Should().Be(Rent + 150_000);
        (await _client.DeleteAsync($"/api/v1/invoices/{again}", s.Token)).IsSuccessStatusCode.Should().BeTrue();
        (await StatusAsync(s, late)).Should().Be("Pending");
    }

    [Fact]
    public async Task ManualLineOnDraft_IsTrackedAsRoomCharge_OversizedCreditWaitsWithWarning()
    {
        var s = await ArrangeAsync();
        var invoiceId = await GenerateAsync(s, s.FirstMonth);

        // 7. Thêm tay trên nháp (quên tạo trước) ⇒ cũng là khoản phát sinh của phòng; chọn đã thanh toán ⇒ không tính tổng.
        var added = await _client.PostJsonAsync($"/api/v1/invoices/{invoiceId}/manual-lines", new
        {
            type = "Surcharge", description = "Thay bóng đèn", amount = 60_000, note = "Người thuê làm vỡ", settled = true, method = "Cash"
        }, s.Token);
        added.StatusCode.Should().Be(HttpStatusCode.OK, await added.Content.ReadAsStringAsync());
        Total(await added.ReadAsync<JsonElement>()).Should().Be(Rent);
        await _client.PostJsonAsync($"/api/v1/invoices/{invoiceId}/manual-lines",
            new { type = "ManualDiscount", description = "Mất nước 2 ngày", amount = 40_000, note = "Sự cố bồn nước" }, s.Token);
        Total(await InvoiceAsync(s, invoiceId)).Should().Be(Rent - 40_000);
        var charges = await (await _client.GetAsync($"/api/v1/rooms/{s.RoomId}/charges", s.Token)).ReadAsync<JsonElement>();
        charges.EnumerateArray().Select(c => (c.GetProperty("kind").GetString(), c.GetProperty("isSettled").GetBoolean(), c.GetProperty("status").GetString()))
            .Should().BeEquivalentTo([("Surcharge", true, "OnDraft"), ("Credit", false, "OnDraft")]);

        // 8. Bù lớn hơn phần thu ⇒ chưa vào phiếu, khoản vẫn chờ, nháp có cảnh báo.
        var huge = (await CreatedAsync(s, "Credit", 5_000_000, s.FirstMonth.AddDays(5))).GetProperty("id").GetGuid();
        (await StatusAsync(s, huge)).Should().Be("Pending");
        var invoice = await InvoiceAsync(s, invoiceId);
        ChargeLine(invoice, huge).Should().BeNull();
        invoice.GetProperty("issues").EnumerateArray().Select(i => i.GetProperty("code").GetString()).Should().Contain("ROOM_CHARGE_NOT_ATTACHED");
    }

    [Fact]
    public async Task MoveOut_ChargeGoesToFinalInvoice_ThenNoOpenInvoice_AndRoomTransferKeepsChargeWithTenant()
    {
        // 9. Đang thanh lý, chưa có phiếu quyết toán ⇒ khoản vào phiếu quyết toán; đã chốt ⇒ không còn phiếu để đưa vào.
        var owner = await _client.CreateActiveOwnerAsync();
        var token = owner.Tokens.AccessToken;
        var today = TestData.Today(factory);
        var start = TestData.StartWithinOnePeriod(today, 10);
        var (propertyId, roomId, _, contractId) = await _client.CreateActiveContractAsync(token, start, anchorDay: start.Day);
        var s = new Setup(token, propertyId, roomId, contractId, start, today);
        await _client.PostJsonAsync($"/api/v1/contracts/{contractId}/liquidation/start", new { actualEndDate = today, reason = "MutualAgreement" }, token);
        var damage = (await CreatedAsync(s, "Surcharge", 300_000, today)).GetProperty("id").GetGuid();
        var final = await (await _client.PostJsonAsync($"/api/v1/contracts/{contractId}/final-invoice", new { }, token)).ReadAsync<JsonElement>();
        ChargeLine(final, damage).Should().NotBeNull("bồi thường = phụ thu trên phiếu quyết toán");
        var finalId = final.GetProperty("summary").GetProperty("id").GetGuid();
        (await _client.PostJsonAsync($"/api/v1/invoices/{finalId}/finalize", null, token)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await (await CreateAsync(s, "Surcharge", 50_000, today)).ReadProblemCodeAsync()).Should().Be("ROOM_CHARGE_NO_OPEN_INVOICE");

        // 10. Chuyển phòng: khoản của phòng cũ trước ngày chuyển thuộc HĐ đã chuyển đi; sau ngày chuyển phòng cũ trống.
        var t = await ArrangeAsync();
        var newRoom = await _client.CreateRoomAsync(t.Token, t.PropertyId);
        var moveDate = t.FirstMonth.AddDays(10);
        (await _client.PostJsonAsync($"/api/v1/contracts/{t.ContractId}/transfer-room", new { toRoomId = newRoom, date = moveDate }, t.Token))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        var before = await CreatedAsync(t, "Surcharge", 120_000, moveDate.AddDays(-3));
        before.GetProperty("contractId").GetGuid().Should().Be(t.ContractId);
        (await (await CreateAsync(t, "Surcharge", 120_000, moveDate.AddDays(3))).ReadProblemCodeAsync())
            .Should().Be("ROOM_CHARGE_NO_TENANT", "phòng cũ đã trống sau ngày chuyển");
        var invoiceId = await GenerateAsync(t, t.FirstMonth);
        ChargeLine(await InvoiceAsync(t, invoiceId), before.GetProperty("id").GetGuid()).Should().NotBeNull("vào phiếu của HĐ đã chuyển sang phòng mới");
    }
}
