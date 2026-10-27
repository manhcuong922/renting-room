using System.Net;
using System.Text.Json;
using renting_room.IntegrationTests.Infrastructure;

namespace renting_room.IntegrationTests;

/// <summary>
/// M07 BL-BR-41 (27/10/2026): hóa đơn trả phòng là bản kết toán — gom nợ các hóa đơn trước và trừ cọc, ra một số cuối. Khu thu sau chốt ngày 1,
/// tiền phòng 3.000.000đ, điện 3.500đ/kWh; HĐ từ ngày 1 của 2 tháng trước, hóa đơn tháng đầu đã chốt nhưng chưa thu; trả phòng ngày 10 tháng sau
/// (theo ngày ở).
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class FinalSettlementTests(ApiFactory factory)
{
    private const decimal Rent = 3_000_000;
    private const decimal Price = 3_500;
    private readonly HttpClient _client = factory.CreateClient();

    private sealed record Setup(
        string Token, Guid ContractId, Guid MeterId, DateOnly Start, DateOnly Today, Guid FirstInvoiceId, decimal FirstTotal, Guid? ReplacedMeterId = null);

    /// <param name="replaceMidMonth">Thay công tơ ngày 15 tháng đầu (công tơ cũ chốt 130, công tơ mới từ 0, cuối tháng 20).</param>
    private async Task<Setup> ArrangeAsync(decimal deposit, bool replaceMidMonth = false)
    {
        var token = (await _client.CreateActiveOwnerAsync()).Tokens.AccessToken;
        var today = TestData.Today(factory);
        var start = new DateOnly(today.Year, today.Month, 1).AddMonths(-2);
        var propertyId = await _client.CreatePropertyAsync(token, anchorDay: 1, chargeMode: "Postpaid", prorationMode: "Daily");
        var roomId = await _client.CreateRoomAsync(token, propertyId);
        var electricity = await _client.FeeIdAsync(token, propertyId, "Điện");
        await _client.PostJsonAsync($"/api/v1/fee-types/{electricity}/prices", new { effectiveFrom = start.AddDays(-30), unitPrice = Price }, token);
        var meterId = await _client.InstallMeterAsync(token, roomId, electricity, start.AddDays(-1), 100);
        var renter = await _client.CreateRenterAsync(token);
        var created = await _client.PostJsonAsync("/api/v1/contracts", new
        {
            roomId,
            contract = new
            {
                representativeRenterId = renter, startDate = start, monthlyRent = Rent, depositAmount = deposit,
                occupants = new[] { new { renterId = renter } }
            }
        }, token);
        created.StatusCode.Should().Be(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        var contractId = await created.ReadIdAsync();
        (await _client.PostJsonAsync($"/api/v1/contracts/{contractId}/activate",
            new { handoverReadings = new[] { new { meterId, value = (decimal?)null } } }, token)).StatusCode.Should().Be(HttpStatusCode.NoContent);

        Guid? replaced = null;
        if (replaceMidMonth)
        {
            var replacement = await _client.PostJsonAsync($"/api/v1/meters/{meterId}/replace",
                new { date = start.AddDays(14), oldFinalValue = 130, newSerialNo = "E-NEW", newInitialValue = 0 }, token);
            replacement.StatusCode.Should().Be(HttpStatusCode.Created, await replacement.Content.ReadAsStringAsync());
            (replaced, meterId) = (meterId, await replacement.ReadIdAsync());
        }
        (await _client.PutJsonAsync($"/api/v1/properties/{propertyId}/meter-readings", new
        {
            readings = new[]
            {
                new { meterId, contractId, closingPeriodStart = start, readingDate = start.AddMonths(1).AddDays(-1), value = replaceMidMonth ? 20m : 150m }
            }
        }, token)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await (await _client.PostJsonAsync("/api/v1/invoices/generate", new { propertyId, billingMonth = $"{start:yyyy-MM}" }, token))
            .ReadAsync<JsonElement>()).GetProperty("created").GetInt32().Should().Be(1);
        var page = await (await _client.GetAsync($"/api/v1/invoices?contractId={contractId}&billingMonth={start:yyyy-MM}", token)).ReadAsync<JsonElement>();
        var firstId = page.GetProperty("items")[0].GetProperty("id").GetGuid();
        var first = await FinalizeAsync(token, firstId);
        return new Setup(token, contractId, meterId, start, today, firstId, Total(first), replaced);
    }

    private async Task<JsonElement> FinalizeAsync(string token, Guid invoiceId)
    {
        var finalized = await _client.PostJsonAsync($"/api/v1/invoices/{invoiceId}/finalize", null, token);
        finalized.StatusCode.Should().Be(HttpStatusCode.OK, await finalized.Content.ReadAsStringAsync());
        return await finalized.ReadAsync<JsonElement>();
    }

    /// <summary>Bắt đầu thanh lý ngày 10 tháng thứ 2 (theo ngày ở) và lập nháp hóa đơn trả phòng (chỉ số cuối 170).</summary>
    private async Task<JsonElement> MoveOutDraftAsync(Setup s)
    {
        (await _client.PostJsonAsync($"/api/v1/contracts/{s.ContractId}/liquidation/start",
            new { actualEndDate = s.Start.AddMonths(1).AddDays(9), reason = "MutualAgreement", finalPeriodCharge = "Daily" }, s.Token))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);
        var created = await _client.PostJsonAsync($"/api/v1/contracts/{s.ContractId}/final-invoice",
            new { finalReadings = new[] { new { meterId = s.MeterId, value = (decimal?)170 } } }, s.Token);
        created.StatusCode.Should().Be(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        return await created.ReadAsync<JsonElement>();
    }

    private async Task<JsonElement> InvoiceAsync(Setup s, Guid id) => await (await _client.GetAsync($"/api/v1/invoices/{id}", s.Token)).ReadAsync<JsonElement>();

    private async Task<JsonElement> DepositAsync(Setup s) =>
        (await (await _client.GetAsync($"/api/v1/contracts/{s.ContractId}", s.Token)).ReadAsync<JsonElement>()).GetProperty("deposit");

    private static Guid Id(JsonElement invoice) => invoice.GetProperty("summary").GetProperty("id").GetGuid();
    private static string? Status(JsonElement invoice) => invoice.GetProperty("summary").GetProperty("paymentStatus").GetString();
    private async Task<string?> StatusAsync(Setup s, Guid id) => Status(await InvoiceAsync(s, id));
    private static decimal Total(JsonElement invoice) => invoice.GetProperty("summary").GetProperty("totalAmount").GetDecimal();
    private static IEnumerable<JsonElement> Lines(JsonElement invoice, string type) =>
        invoice.GetProperty("lines").EnumerateArray().Where(l => l.GetProperty("type").GetString() == type);
    private static decimal Charges(JsonElement invoice) =>
        invoice.GetProperty("lines").EnumerateArray().Where(l => l.GetProperty("type").GetString() is not ("PreviousDebt" or "Deposit"))
            .Sum(l => l.GetProperty("amount").GetDecimal());

    [Fact]
    public async Task FinalInvoice_CarriesUnpaidInvoiceAndDeposit_FinalizeSettlesThem_SurplusDepositIsRefunded()
    {
        var s = await ArrangeAsync(deposit: 6_000_000);

        var draft = await MoveOutDraftAsync(s);

        var debt = Lines(draft, "PreviousDebt").Single();
        debt.GetProperty("amount").GetDecimal().Should().Be(s.FirstTotal, "nợ hóa đơn tháng đầu chuyển vào hóa đơn trả phòng");
        debt.GetProperty("sourceInvoiceId").GetGuid().Should().Be(s.FirstInvoiceId);
        Lines(draft, "Deposit").Single().GetProperty("amount").GetDecimal().Should().Be(-6_000_000);
        draft.GetProperty("subtotal").GetDecimal().Should().Be(Charges(draft), "nợ chuyển và cọc không thuộc phần thu");
        draft.GetProperty("settlementTotal").GetDecimal().Should().Be(s.FirstTotal - 6_000_000);
        Total(draft).Should().Be(Charges(draft) + draft.GetProperty("summary").GetProperty("roundingAmount").GetDecimal() + s.FirstTotal - 6_000_000);
        var detail = await InvoiceAsync(s, Id(draft));
        detail.GetProperty("previousDebts").EnumerateArray().Should().BeEmpty("nợ cũ đã nằm trong hóa đơn trả phòng — không hiện / cộng lần 2");

        var final = await FinalizeAsync(s.Token, Id(draft));

        var first = await InvoiceAsync(s, s.FirstInvoiceId);
        Status(first).Should().Be("CarriedOver", "hóa đơn trả phòng chưa xong ⇒ chờ thu ở đó");
        first.GetProperty("summary").GetProperty("carriedToInvoiceNo").GetString().Should().Be(final.GetProperty("summary").GetProperty("invoiceNo").GetString());
        first.GetProperty("summary").GetProperty("invoiceAmount").GetDecimal().Should().Be(s.FirstTotal, "hóa đơn thường: Tổng hóa đơn = Tổng thu");
        first.GetProperty("summary").GetProperty("outstanding").GetDecimal().Should().Be(0);
        first.GetProperty("summary").GetProperty("collectedAmount").GetDecimal().Should().Be(0, "chuyển nợ không phải tiền đã thu");
        first.GetProperty("carriedOverAmount").GetDecimal().Should().Be(s.FirstTotal);
        first.GetProperty("carriedToInvoiceId").GetGuid().Should().Be(Id(final));
        var deposit = await DepositAsync(s);
        deposit.GetProperty("status").GetString().Should().Be("Deducted");
        deposit.GetProperty("refundedAmount").GetDecimal().Should().Be(6_000_000);
        Total(final).Should().BeNegative();
        final.GetProperty("summary").GetProperty("paymentStatus").GetString().Should().Be("RefundPending", "cọc dư phải trả lại người thuê");
        final.GetProperty("summary").GetProperty("refundDue").GetDecimal().Should().Be(-Total(final));
        final.GetProperty("summary").GetProperty("invoiceAmount").GetDecimal()
            .Should().Be(Total(final) - final.GetProperty("settlementTotal").GetDecimal(), "Tổng hóa đơn = tiền kỳ cuối, Tổng thu = sau kết toán")
            .And.Be(Charges(final) + final.GetProperty("summary").GetProperty("roundingAmount").GetDecimal());

        // Trạng thái hóa đơn nguồn đi theo hóa đơn trả phòng (BL-BR-41, 27/10/2026).
        (await _client.PostJsonAsync($"/api/v1/invoices/{Id(final)}/refund", new { refundedOn = s.Today, method = "Cash" }, s.Token))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        (await StatusAsync(s, s.FirstInvoiceId)).Should().Be("CarriedSettled", "đã trả lại cọc dư ⇒ kết toán xong");
        var listed = await (await _client.GetAsync($"/api/v1/invoices?contractId={s.ContractId}", s.Token)).ReadAsync<JsonElement>();
        listed.GetProperty("items").EnumerateArray().Single(i => i.GetProperty("id").GetGuid() == s.FirstInvoiceId)
            .GetProperty("paymentStatus").GetString().Should().Be("CarriedSettled");
        (await _client.DeleteAsync($"/api/v1/invoices/{Id(final)}/refund", s.Token)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await StatusAsync(s, s.FirstInvoiceId)).Should().Be("CarriedOver", "bỏ xác nhận hoàn ⇒ lại chờ");

        var completed = await _client.PostJsonAsync($"/api/v1/contracts/{s.ContractId}/liquidation/complete",
            new { refund = new { refundedOn = s.Today, method = "Cash" } }, s.Token);
        completed.StatusCode.Should().Be(HttpStatusCode.NoContent, await completed.Content.ReadAsStringAsync());
        (await StatusAsync(s, s.FirstInvoiceId)).Should().Be("CarriedSettled");
    }

    [Fact]
    public async Task DepositSmallerThanWhatIsOwed_TenantPaysTheRest_TotalsCountTheDebtOnce()
    {
        var s = await ArrangeAsync(deposit: 1_000_000);

        var final = await FinalizeAsync(s.Token, Id(await MoveOutDraftAsync(s)));

        Total(final).Should().BePositive();
        var own = Total(final) - final.GetProperty("settlementTotal").GetDecimal();
        var totals = await (await _client.GetAsync($"/api/v1/invoices/totals?contractId={s.ContractId}", s.Token)).ReadAsync<JsonElement>();
        totals.GetProperty("totalAmount").GetDecimal().Should().Be(s.FirstTotal + own, "nợ tháng đầu không đếm 2 lần");
        totals.GetProperty("paidAmount").GetDecimal().Should().Be(0, "chuyển nợ / trừ cọc không phải đã thu");
        totals.GetProperty("outstandingAmount").GetDecimal().Should().Be(Total(final));

        (await _client.PostJsonAsync($"/api/v1/contracts/{s.ContractId}/payments",
            new { amount = 100_000, method = "Cash", paidAt = s.Today, invoiceId = Id(final) }, s.Token)).StatusCode.Should().Be(HttpStatusCode.Created);
        (await StatusAsync(s, s.FirstInvoiceId)).Should().Be("CarriedOver", "hóa đơn trả phòng mới thu một phần");

        var complete = $"/api/v1/contracts/{s.ContractId}/liquidation/complete";
        (await (await _client.PostJsonAsync(complete, null, s.Token)).ReadProblemCodeAsync()).Should().Be("CONTRACT_HAS_DEBT");
        (await _client.PostJsonAsync(complete, new { settlement = "CollectAll" }, s.Token)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await InvoiceAsync(s, Id(final))).GetProperty("summary").GetProperty("collectedAmount").GetDecimal().Should().Be(Total(final));
        (await StatusAsync(s, s.FirstInvoiceId)).Should().Be("CarriedSettled", "thu đủ hóa đơn trả phòng ⇒ hóa đơn nợ hoàn thành");
    }

    [Fact]
    public async Task ForfeitBeforeFinalizing_RemovesDepositLine_UndoBringsItBack_DeductedDepositIsLocked()
    {
        var s = await ArrangeAsync(deposit: 3_000_000);
        var draft = await MoveOutDraftAsync(s);

        (await _client.PostJsonAsync($"/api/v1/contracts/{s.ContractId}/deposit/forfeit",
            new { date = s.Today, note = "Trả phòng trước hạn, mất cọc theo HĐ" }, s.Token)).IsSuccessStatusCode.Should().BeTrue();
        Lines(await InvoiceAsync(s, Id(draft)), "Deposit").Should().BeEmpty("không trả cọc ⇒ nháp tự tính lại, bỏ dòng trừ cọc");
        (await _client.DeleteAsync($"/api/v1/contracts/{s.ContractId}/deposit/refund", s.Token)).IsSuccessStatusCode.Should().BeTrue();
        Lines(await InvoiceAsync(s, Id(draft)), "Deposit").Single().GetProperty("amount").GetDecimal().Should().Be(-3_000_000);

        await FinalizeAsync(s.Token, Id(draft));

        (await DepositAsync(s)).GetProperty("status").GetString().Should().Be("Deducted");
        (await (await _client.PostJsonAsync($"/api/v1/contracts/{s.ContractId}/deposit/forfeit",
            new { date = s.Today, note = "Lần 2" }, s.Token)).ReadProblemCodeAsync()).Should().Be("DEPOSIT_DEDUCTED");
        (await (await _client.DeleteAsync($"/api/v1/contracts/{s.ContractId}/deposit/refund", s.Token)).ReadProblemCodeAsync())
            .Should().Be("DEPOSIT_DEDUCTED");
    }

    [Fact]
    public async Task DebtPaidAfterDraft_FinalizeAsksToRecalculate_ThenNoDebtLine_NoDepositNoDepositLine()
    {
        var s = await ArrangeAsync(deposit: 0);
        var draft = await MoveOutDraftAsync(s);
        Lines(draft, "PreviousDebt").Should().ContainSingle();
        Lines(draft, "Deposit").Should().BeEmpty("HĐ không cọc");

        (await _client.PostJsonAsync($"/api/v1/contracts/{s.ContractId}/payments",
            new { amount = s.FirstTotal, method = "Cash", paidAt = s.Today, invoiceId = s.FirstInvoiceId }, s.Token)).StatusCode.Should().Be(HttpStatusCode.Created);

        (await (await _client.PostJsonAsync($"/api/v1/invoices/{Id(draft)}/finalize", null, s.Token)).ReadProblemCodeAsync()).Should().Be("DRAFT_STALE");
        (await _client.PostJsonAsync("/api/v1/invoices/recalculate", new { invoiceIds = new[] { Id(draft) } }, s.Token)).IsSuccessStatusCode.Should().BeTrue();
        Lines(await InvoiceAsync(s, Id(draft)), "PreviousDebt").Should().BeEmpty("đã thu đủ ⇒ không còn nợ để chuyển");
        await FinalizeAsync(s.Token, Id(draft));
    }

    [Fact]
    public async Task SettlementLinesLocked_CarriedInvoicePaymentNotReversible_VoidingFinalRestoresDebtAndDeposit()
    {
        var s = await ArrangeAsync(deposit: 3_000_000);
        var paid = await _client.PostJsonAsync($"/api/v1/contracts/{s.ContractId}/payments",
            new { amount = 1_000_000, method = "Cash", paidAt = s.Today, invoiceId = s.FirstInvoiceId }, s.Token);
        paid.StatusCode.Should().Be(HttpStatusCode.Created, await paid.Content.ReadAsStringAsync());
        var paymentId = await paid.ReadIdAsync();
        var draft = await MoveOutDraftAsync(s);
        Lines(draft, "PreviousDebt").Single().GetProperty("amount").GetDecimal().Should().Be(s.FirstTotal - 1_000_000, "chỉ chuyển phần còn nợ");

        var deposit = Lines(draft, "Deposit").Single().GetProperty("id").GetGuid();
        (await (await _client.PutJsonAsync($"/api/v1/invoices/{Id(draft)}/lines/{deposit}", new { amount = 0, note = "Không trừ" }, s.Token))
            .ReadProblemCodeAsync()).Should().Be("SETTLEMENT_LINE_LOCKED");
        (await (await _client.DeleteAsync($"/api/v1/invoices/{Id(draft)}/lines/{deposit}", s.Token)).ReadProblemCodeAsync())
            .Should().Be("SETTLEMENT_LINE_LOCKED");

        var final = await FinalizeAsync(s.Token, Id(draft));
        (await (await _client.PostJsonAsync($"/api/v1/payments/{paymentId}/reverse", new { reason = "Nhập nhầm" }, s.Token)).ReadProblemCodeAsync())
            .Should().Be("INVOICE_CARRIED_OVER");

        var voided = await _client.PostJsonAsync($"/api/v1/invoices/{Id(final)}/void", new { reason = "Lập sai" }, s.Token);
        voided.StatusCode.Should().Be(HttpStatusCode.OK, await voided.Content.ReadAsStringAsync());
        var first = await InvoiceAsync(s, s.FirstInvoiceId);
        first.GetProperty("summary").GetProperty("outstanding").GetDecimal().Should().Be(s.FirstTotal - 1_000_000, "hủy hóa đơn trả phòng ⇒ nợ quay lại");
        first.GetProperty("carriedOverAmount").GetDecimal().Should().Be(0);
        (await DepositAsync(s)).GetProperty("status").GetString().Should().Be("Holding");
    }

    /// <summary>
    /// Số chuyển cố định lúc chốt ⇒ không tính lại hóa đơn nguồn. Thay công tơ giữa tháng: số cuối công tơ cũ không là chỉ số đầu của hóa đơn
    /// trả phòng (tính từ công tơ mới) nên <c>READING_NEXT_PERIOD_FINALIZED</c> không chặn — phải chặn theo hóa đơn đã chuyển nợ.
    /// </summary>
    [Fact]
    public async Task MeterReplacedMidMonth_AdjustingOldMeterFinalOfCarriedInvoice_IsRefused_UntilMoveOutInvoiceIsVoided()
    {
        var s = await ArrangeAsync(deposit: 3_000_000, replaceMidMonth: true);
        var final = await FinalizeAsync(s.Token, Id(await MoveOutDraftAsync(s)));
        var removal = (await (await _client.GetAsync($"/api/v1/meters/{s.ReplacedMeterId}/readings", s.Token)).ReadAsync<List<JsonElement>>())
            .Single(r => r.GetProperty("kind").GetString() == "Removal").GetProperty("id").GetGuid();
        Task<HttpResponseMessage> AdjustAsync(decimal value) => _client.PostJsonAsync($"/api/v1/meter-readings/{removal}/adjust",
            new { value, reason = "Gõ nhầm số cuối công tơ cũ", handling = "Immediate" }, s.Token);

        (await (await AdjustAsync(125)).ReadProblemCodeAsync()).Should().Be("INVOICE_CARRIED_OVER", "giảm ⇒ hoàn tiền người thuê chưa trả");
        (await (await AdjustAsync(135)).ReadProblemCodeAsync()).Should().Be("INVOICE_CARRIED_OVER", "tăng ⇒ nợ mới nằm ngoài kết toán");
        Total(await InvoiceAsync(s, s.FirstInvoiceId)).Should().Be(s.FirstTotal, "nợ đã chuyển theo số lúc chốt — hóa đơn nguồn giữ nguyên");

        (await _client.PostJsonAsync($"/api/v1/invoices/{Id(final)}/void", new { reason = "Sửa số công tơ cũ" }, s.Token))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        var adjusted = await AdjustAsync(125);
        adjusted.StatusCode.Should().Be(HttpStatusCode.OK, await adjusted.Content.ReadAsStringAsync());
        Lines(await InvoiceAsync(s, s.FirstInvoiceId), "Metered").Single().GetProperty("quantity").GetDecimal()
            .Should().Be(25 + 20, "công tơ cũ 100 → 125, công tơ mới 0 → 20");
    }
}
