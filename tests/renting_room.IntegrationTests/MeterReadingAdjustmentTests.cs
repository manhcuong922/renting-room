using System.Net;
using System.Text.Json;
using renting_room.IntegrationTests.Infrastructure;

namespace renting_room.IntegrationTests;

/// <summary>
/// M06 MT-UC-09 / MT-BR-19 (viết lại 20/10/2026): điều chỉnh chỉ số đã chốt — chưa thu ⇒ tính lại trên hóa đơn; đã thu ⇒ Xử lý ngay
/// (hoàn / thu thêm, trừ bỏ nợ trước) hoặc Đẩy sang hóa đơn sau (1 khoản khóa, điều chỉnh lại sửa chính khoản). Điện 3.800đ/kWh, tiền phòng 3,5tr.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class MeterReadingAdjustmentTests(ApiFactory factory)
{
    private const decimal Price = 3800;
    private readonly HttpClient _client = factory.CreateClient();

    private sealed record Setup(string Token, Guid PropertyId, Guid ContractId, Guid MeterId, DateOnly Previous, DateOnly Month, DateOnly Today);

    /// <summary>Khu thu sau chốt ngày 1, HĐ từ đầu tháng trước, công tơ điện nhận phòng ở <paramref name="handover"/>.</summary>
    private async Task<Setup> ArrangeAsync(decimal handover = 1250, object? price = null)
    {
        var owner = await _client.CreateActiveOwnerAsync();
        var token = owner.Tokens.AccessToken;
        var today = TestData.Today(factory);
        var month = new DateOnly(today.Year, today.Month, 1);
        var previous = month.AddMonths(-1);
        var propertyId = await _client.CreatePropertyAsync(token, anchorDay: 1, chargeMode: "Postpaid");
        var electricity = await _client.FeeIdAsync(token, propertyId, "Điện");
        (await _client.PostJsonAsync($"/api/v1/fee-types/{electricity}/prices",
            price ?? new { effectiveFrom = new DateOnly(2026, 1, 1), unitPrice = Price }, token)).IsSuccessStatusCode.Should().BeTrue();
        var roomId = await _client.CreateRoomAsync(token, propertyId);
        var meterId = await _client.InstallMeterAsync(token, roomId, electricity, previous.AddDays(-1), handover);
        var contractId = await _client.CreateContractAsync(token, roomId, await _client.CreateRenterAsync(token), previous);
        (await _client.PostJsonAsync($"/api/v1/contracts/{contractId}/activate",
            new { handoverReadings = new[] { new { meterId, value = (decimal?)null } } }, token)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        return new Setup(token, propertyId, contractId, meterId, previous, month, today);
    }

    private Task<HttpResponseMessage> SaveAsync(Setup s, DateOnly closing, DateOnly on, decimal value) =>
        _client.PutJsonAsync($"/api/v1/properties/{s.PropertyId}/meter-readings",
            new { readings = new[] { new { meterId = s.MeterId, contractId = s.ContractId, closingPeriodStart = closing, readingDate = on, value } } }, s.Token);

    private async Task<Guid> PeriodicIdAsync(Setup s, decimal value) =>
        (await (await _client.GetAsync($"/api/v1/meters/{s.MeterId}/readings", s.Token)).ReadAsync<List<JsonElement>>())
        .Single(r => r.GetProperty("kind").GetString() == "Periodic" && r.GetProperty("value").GetDecimal() == value).GetProperty("id").GetGuid();

    private async Task<JsonElement> GenerateAsync(Setup s, DateOnly billingMonth)
    {
        (await (await _client.PostJsonAsync("/api/v1/invoices/generate", new { propertyId = s.PropertyId, billingMonth = $"{billingMonth:yyyy-MM}" }, s.Token))
            .ReadAsync<JsonElement>()).GetProperty("created").GetInt32().Should().Be(1);
        var page = await (await _client.GetAsync($"/api/v1/invoices?contractId={s.ContractId}&billingMonth={billingMonth:yyyy-MM}", s.Token)).ReadAsync<JsonElement>();
        return await InvoiceAsync(s, page.GetProperty("items")[0].GetProperty("id").GetGuid());
    }

    private async Task<JsonElement> InvoiceAsync(Setup s, Guid id) =>
        await (await _client.GetAsync($"/api/v1/invoices/{id}", s.Token)).ReadAsync<JsonElement>();

    private static Guid Id(JsonElement invoice) => invoice.GetProperty("summary").GetProperty("id").GetGuid();
    private static decimal Total(JsonElement invoice) => invoice.GetProperty("summary").GetProperty("totalAmount").GetDecimal();
    private static JsonElement Metered(JsonElement invoice) =>
        invoice.GetProperty("lines").EnumerateArray().Single(l => l.GetProperty("type").GetString() == "Metered");

    private async Task FinalizeAsync(Setup s, Guid invoiceId)
    {
        var finalized = await _client.PostJsonAsync($"/api/v1/invoices/{invoiceId}/finalize", null, s.Token);
        finalized.StatusCode.Should().Be(HttpStatusCode.OK, await finalized.Content.ReadAsStringAsync());
    }

    private async Task PayAsync(Setup s, Guid invoiceId, decimal amount) =>
        (await _client.PostJsonAsync($"/api/v1/contracts/{s.ContractId}/payments",
            new { amount, method = "Cash", paidAt = s.Today, invoiceId }, s.Token)).StatusCode.Should().Be(HttpStatusCode.Created);

    /// <summary>Tháng trước chốt với chỉ số gõ nhầm <paramref name="typed"/> — trả id hóa đơn và id chỉ số.</summary>
    private async Task<(Guid InvoiceId, Guid ReadingId)> FinalizedPreviousAsync(Setup s, decimal typed)
    {
        (await SaveAsync(s, s.Previous, s.Month, typed)).StatusCode.Should().Be(HttpStatusCode.OK);
        var invoice = await GenerateAsync(s, s.Previous);
        await FinalizeAsync(s, Id(invoice));
        return (Id(invoice), await PeriodicIdAsync(s, typed));
    }

    private Task<HttpResponseMessage> AdjustAsync(Setup s, Guid readingId, decimal value, string? handling = null, object? settlement = null) =>
        _client.PostJsonAsync($"/api/v1/meter-readings/{readingId}/adjust",
            new { value, reason = "Gõ nhầm chỉ số khi ghi", handling, settlement }, s.Token);

    private static async Task<JsonElement> OkAsync(HttpResponseMessage response)
    {
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return await response.ReadAsync<JsonElement>();
    }

    [Fact]
    public async Task Unpaid_RecalculatedOnTheInvoice_KeepsNumber_FlagsResend_RecordsHistory()
    {
        var s = await ArrangeAsync();
        (await SaveAsync(s, s.Previous, s.Month, 1388)).StatusCode.Should().Be(HttpStatusCode.OK);
        var notLocked = await PeriodicIdAsync(s, 1388);
        (await (await AdjustAsync(s, notLocked, 1338)).ReadProblemCodeAsync()).Should().Be("READING_NOT_LOCKED", "chưa chốt thì sửa trực tiếp");
        var draft = await GenerateAsync(s, s.Previous);
        await FinalizeAsync(s, Id(draft));
        var before = await InvoiceAsync(s, Id(draft));
        var handover = Metered(before).GetProperty("segments")[0].GetProperty("startReadingId").GetGuid();

        (await (await _client.PutJsonAsync($"/api/v1/meter-readings/{notLocked}", new { value = 1338 }, s.Token)).ReadProblemCodeAsync())
            .Should().Be("READING_LOCKED");
        (await (await AdjustAsync(s, handover, 1240)).ReadProblemCodeAsync()).Should().Be("READING_NOT_ADJUSTABLE");
        (await (await AdjustAsync(s, notLocked, 1388)).ReadProblemCodeAsync()).Should().Be("READING_UNCHANGED");

        var result = await OkAsync(await AdjustAsync(s, notLocked, 1338));

        result.GetProperty("handling").GetString().Should().Be("Recalculated");
        result.GetProperty("charge").ValueKind.Should().Be(JsonValueKind.Null);
        result.GetProperty("payments").GetArrayLength().Should().Be(0);
        var after = result.GetProperty("invoice");
        Metered(after).GetProperty("quantity").GetDecimal().Should().Be(88);
        Metered(after).GetProperty("amount").GetDecimal().Should().Be(88 * Price);
        Total(after).Should().Be(Total(before) - 50 * Price);
        after.GetProperty("summary").GetProperty("invoiceNo").GetString().Should().Be(before.GetProperty("summary").GetProperty("invoiceNo").GetString());
        after.GetProperty("summary").GetProperty("isAdjusted").GetBoolean().Should().BeTrue();
        after.GetProperty("summary").GetProperty("needsResend").GetBoolean().Should().BeTrue();

        var history = await (await _client.GetAsync($"/api/v1/invoices/{Id(draft)}/adjustments", s.Token)).ReadAsync<List<JsonElement>>();
        history.Should().ContainSingle(h => h.GetProperty("handling").GetString() == "Recalculated"
            && h.GetProperty("oldValue").GetDecimal() == 1388 && h.GetProperty("newValue").GetDecimal() == 1338
            && h.GetProperty("difference").GetDecimal() == -50 * Price);
        var resend = await (await _client.GetAsync($"/api/v1/invoices?contractId={s.ContractId}&needsResend=true", s.Token)).ReadAsync<JsonElement>();
        resend.GetProperty("items").GetArrayLength().Should().Be(1);
        var resent = await OkAsync(await _client.PostJsonAsync($"/api/v1/invoices/{Id(draft)}/resent", null, s.Token));
        resent.GetProperty("summary").GetProperty("needsResend").GetBoolean().Should().BeFalse();

        (await SaveAsync(s, s.Month, s.Today, 1400)).StatusCode.Should().Be(HttpStatusCode.OK);
        var next = await GenerateAsync(s, s.Month);
        Metered(next).GetProperty("quantity").GetDecimal().Should().Be(62, "tháng sau tính từ số đã điều chỉnh");
        (await (await _client.PostJsonAsync($"/api/v1/invoices/{Id(next)}/resent", null, s.Token)).ReadProblemCodeAsync())
            .Should().Be("INVOICE_NOT_FINALIZED");
    }

    [Fact]
    public async Task Paid_HandlingRequired_ImmediateWritesRefund_ReversingRefundIsRefused()
    {
        var s = await ArrangeAsync();
        var (invoiceId, readingId) = await FinalizedPreviousAsync(s, 1388);
        var before = await InvoiceAsync(s, invoiceId);
        await PayAsync(s, invoiceId, Total(before));

        (await (await AdjustAsync(s, readingId, 1338)).ReadProblemCodeAsync()).Should().Be("ADJUSTMENT_HANDLING_REQUIRED");
        var result = await OkAsync(await AdjustAsync(s, readingId, 1338, "Immediate", new { date = s.Today, method = "BankTransfer" }));

        result.GetProperty("handling").GetString().Should().Be("Immediate");
        var refund = result.GetProperty("payments").EnumerateArray().Single();
        refund.GetProperty("kind").GetString().Should().Be("Refund");
        refund.GetProperty("receiptNo").GetString().Should().StartWith("PC");
        refund.GetProperty("amount").GetDecimal().Should().Be(50 * Price);
        refund.GetProperty("method").GetString().Should().Be("BankTransfer");
        var summary = result.GetProperty("invoice").GetProperty("summary");
        summary.GetProperty("totalAmount").GetDecimal().Should().Be(Total(before) - 50 * Price);
        summary.GetProperty("collectedAmount").GetDecimal().Should().Be(summary.GetProperty("totalAmount").GetDecimal());
        summary.GetProperty("paymentStatus").GetString().Should().Be("Paid");

        var reverse = await _client.PostJsonAsync($"/api/v1/payments/{refund.GetProperty("id").GetGuid()}/reverse", new { reason = "Thử" }, s.Token);
        (await reverse.ReadProblemCodeAsync()).Should().Be("PAYMENT_EXCEEDS_DEBT", "đảo phiếu hoàn làm đã thu vượt tổng mới");
    }

    [Fact]
    public async Task Paid_NextInvoice_OneLockedChargeRevisedThenCancelled_NextDraftRecalculated()
    {
        var s = await ArrangeAsync();
        var (invoiceId, readingId) = await FinalizedPreviousAsync(s, 1388);
        var before = await InvoiceAsync(s, invoiceId);
        await PayAsync(s, invoiceId, Total(before));
        (await SaveAsync(s, s.Month, s.Today, 1400)).StatusCode.Should().Be(HttpStatusCode.OK);
        var nextId = Id(await GenerateAsync(s, s.Month));

        var first = await OkAsync(await AdjustAsync(s, readingId, 1338, "NextInvoice"));
        var charge = first.GetProperty("charge");
        charge.GetProperty("kind").GetString().Should().Be("Credit");
        charge.GetProperty("amount").GetDecimal().Should().Be(50 * Price);
        charge.GetProperty("status").GetString().Should().Be("OnDraft");
        charge.GetProperty("isSystem").GetBoolean().Should().BeTrue();
        Total(first.GetProperty("invoice")).Should().Be(Total(before), "hóa đơn nguồn giữ nguyên tổng");
        first.GetProperty("invoice").GetProperty("summary").GetProperty("isAdjusted").GetBoolean().Should().BeTrue();
        first.GetProperty("invoice").GetProperty("summary").GetProperty("needsResend").GetBoolean().Should().BeFalse();
        var next = await InvoiceAsync(s, nextId);
        Metered(next).GetProperty("quantity").GetDecimal().Should().Be(62, "nháp tháng sau tự tính lại từ 1.338");
        next.GetProperty("summary").GetProperty("isStale").GetBoolean().Should().BeFalse();
        var chargeId = charge.GetProperty("id").GetGuid();

        (await (await _client.PostJsonAsync($"/api/v1/room-charges/{chargeId}/cancel", new { reason = "Thử" }, s.Token)).ReadProblemCodeAsync())
            .Should().Be("ROOM_CHARGE_SYSTEM");

        var second = await OkAsync(await AdjustAsync(s, readingId, 1350, "NextInvoice"));
        second.GetProperty("charge").GetProperty("id").GetGuid().Should().Be(chargeId, "lần sau sửa chính khoản đó");
        second.GetProperty("charge").GetProperty("amount").GetDecimal().Should().Be(38 * Price);
        next = await InvoiceAsync(s, nextId);
        Metered(next).GetProperty("quantity").GetDecimal().Should().Be(50);
        next.GetProperty("lines").EnumerateArray().Should().ContainSingle(l => l.GetProperty("type").GetString() == "ManualDiscount")
            .Which.GetProperty("amount").GetDecimal().Should().Be(-38 * Price);

        var back = await OkAsync(await AdjustAsync(s, readingId, 1388, "NextInvoice"));
        back.GetProperty("charge").ValueKind.Should().Be(JsonValueKind.Null, "về đúng số lúc chốt ⇒ khoản tự hủy");
        next = await InvoiceAsync(s, nextId);
        next.GetProperty("lines").EnumerateArray().Should().NotContain(l => l.GetProperty("type").GetString() == "ManualDiscount");
        (await (await _client.GetAsync($"/api/v1/invoices/{invoiceId}/adjustments", s.Token)).ReadAsync<List<JsonElement>>()).Should().HaveCount(3);
    }

    [Fact]
    public async Task Undercharged_ImmediateWithoutSettlementLeavesDebt_WithSettlementCollects_NextInvoiceTieredSurcharge()
    {
        var debt = await ArrangeAsync();
        var (debtInvoice, debtReading) = await FinalizedPreviousAsync(debt, 1300);
        var total = Total(await InvoiceAsync(debt, debtInvoice));
        await PayAsync(debt, debtInvoice, total);
        var owed = (await OkAsync(await AdjustAsync(debt, debtReading, 1338, "Immediate"))).GetProperty("invoice").GetProperty("summary");
        owed.GetProperty("outstanding").GetDecimal().Should().Be(owed.GetProperty("totalAmount").GetDecimal() - total, "phần thiếu thành nợ");
        owed.GetProperty("outstanding").GetDecimal().Should().BeGreaterThan(0);
        owed.GetProperty("paymentStatus").GetString().Should().NotBe("Paid");

        var collect = await ArrangeAsync();
        var (collectInvoice, collectReading) = await FinalizedPreviousAsync(collect, 1300);
        await PayAsync(collect, collectInvoice, Total(await InvoiceAsync(collect, collectInvoice)));
        var collected = await OkAsync(await AdjustAsync(collect, collectReading, 1338, "Immediate", new { date = collect.Today, method = "Cash" }));
        collected.GetProperty("payments").EnumerateArray().Single().GetProperty("kind").GetString().Should().Be("Receipt");
        collected.GetProperty("invoice").GetProperty("summary").GetProperty("paymentStatus").GetString().Should().Be("Paid");

        var tiered = await ArrangeAsync(100, new
        {
            effectiveFrom = new DateOnly(2026, 1, 1), tiers = new object[] { new { upTo = 50, price = 2000 }, new { upTo = (int?)null, price = 3000 } }
        });
        var (tieredInvoice, tieredReading) = await FinalizedPreviousAsync(tiered, 150); // 50 kWh × 2.000 = 100.000, đúng là 180
        await PayAsync(tiered, tieredInvoice, Total(await InvoiceAsync(tiered, tieredInvoice)));
        var surcharge = (await OkAsync(await AdjustAsync(tiered, tieredReading, 180, "NextInvoice"))).GetProperty("charge");
        surcharge.GetProperty("kind").GetString().Should().Be("Surcharge");
        surcharge.GetProperty("amount").GetDecimal().Should().Be(50 * 2000 + 30 * 3000 - 100_000, "bậc tính trên tổng của dòng theo bộ giá đã lưu");
    }

    [Fact]
    public async Task WrittenOff_OverchargeReducesWriteOffFirst_NoRefund_NextInvoiceHasNoCredit()
    {
        var s = await ArrangeAsync();
        var (invoiceId, readingId) = await FinalizedPreviousAsync(s, 1388);
        var total = Total(await InvoiceAsync(s, invoiceId));
        (await _client.PostJsonAsync($"/api/v1/invoices/{invoiceId}/pay-and-write-off",
            new { amount = total - 200_000, method = "Cash", paidAt = s.Today, reason = "Mất nước 2 ngày" }, s.Token))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        var pushed = await OkAsync(await AdjustAsync(s, readingId, 1338, "NextInvoice"));
        pushed.GetProperty("charge").ValueKind.Should().Be(JsonValueKind.Null, "190.000đ thừa nằm trong 200.000đ chủ trọ đã cho");

        var result = await OkAsync(await AdjustAsync(s, readingId, 1338.5m, "Immediate"));
        var summary = result.GetProperty("invoice").GetProperty("summary");
        summary.GetProperty("writtenOffAmount").GetDecimal().Should().Be(200_000 - (total - summary.GetProperty("totalAmount").GetDecimal()));
        summary.GetProperty("collectedAmount").GetDecimal().Should().Be(total - 200_000, "tiền người thuê trả thật không đổi");
        result.GetProperty("payments").EnumerateArray().Select(p => p.GetProperty("kind").GetString()).Should().NotContain("Refund");
        result.GetProperty("payments").EnumerateArray().Should().Contain(p => p.GetProperty("status").GetString() == "Reversed");
    }

    [Fact]
    public async Task Liquidating_NextInvoiceGoesToMoveOutInvoice_FinalizedMoveOutOnlyImmediate_EndedGoesAfterEnd()
    {
        var s = await ArrangeAsync();
        var (invoiceId, readingId) = await FinalizedPreviousAsync(s, 1388);
        await PayAsync(s, invoiceId, Total(await InvoiceAsync(s, invoiceId)));
        (await _client.PostJsonAsync($"/api/v1/contracts/{s.ContractId}/liquidation/start",
            new { actualEndDate = s.Today, reason = "MutualAgreement" }, s.Token)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        var moveOut = await _client.PostJsonAsync($"/api/v1/contracts/{s.ContractId}/final-invoice",
            new { finalReadings = new[] { new { meterId = s.MeterId, value = 1400m } } }, s.Token);
        moveOut.StatusCode.Should().Be(HttpStatusCode.Created, await moveOut.Content.ReadAsStringAsync());
        var moveOutId = Id(await moveOut.ReadAsync<JsonElement>());

        var pushed = await OkAsync(await AdjustAsync(s, readingId, 1338, "NextInvoice"));
        pushed.GetProperty("charge").GetProperty("invoiceId").GetGuid().Should().Be(moveOutId, "hóa đơn sau = hóa đơn trả phòng");
        Metered(await InvoiceAsync(s, moveOutId)).GetProperty("quantity").GetDecimal().Should().Be(62);

        await FinalizeAsync(s, moveOutId);
        var moveOutTotal = Total(await InvoiceAsync(s, moveOutId));
        await PayAsync(s, moveOutId, moveOutTotal);
        var finalReading = Metered(await InvoiceAsync(s, moveOutId)).GetProperty("segments")[0].GetProperty("endReadingId").GetGuid();
        (await (await AdjustAsync(s, finalReading, 1390, "NextInvoice")).ReadProblemCodeAsync()).Should().Be("ADJUSTMENT_NO_NEXT_INVOICE");
        var immediate = await OkAsync(await AdjustAsync(s, finalReading, 1390, "Immediate"));
        immediate.GetProperty("payments").EnumerateArray().Single().GetProperty("amount").GetDecimal().Should().Be(10 * Price);

        (await _client.PostJsonAsync($"/api/v1/contracts/{s.ContractId}/liquidation/complete", null, s.Token))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);
        var afterEnd = await OkAsync(await AdjustAsync(s, finalReading, 1385, "Immediate"));
        afterEnd.GetProperty("handling").GetString().Should().Be("AfterEnd", "HĐ đã kết thúc ⇒ Chênh lệch sau thanh lý (Lượt 2)");
        afterEnd.GetProperty("payments").EnumerateArray().Should().BeEmpty();
    }

    [Fact]
    public async Task VoidingSourceInvoice_CancelsPendingAdjustmentCharge()
    {
        var s = await ArrangeAsync();
        var (invoiceId, readingId) = await FinalizedPreviousAsync(s, 1388);
        await PayAsync(s, invoiceId, Total(await InvoiceAsync(s, invoiceId)));
        var charge = (await OkAsync(await AdjustAsync(s, readingId, 1338, "NextInvoice"))).GetProperty("charge");
        charge.GetProperty("status").GetString().Should().Be("Pending");

        var payment = (await (await _client.GetAsync($"/api/v1/payments?invoiceId={invoiceId}", s.Token)).ReadAsync<List<JsonElement>>()).Single();
        (await _client.PostJsonAsync($"/api/v1/payments/{payment.GetProperty("id").GetGuid()}/reverse", new { reason = "Nhập nhầm" }, s.Token))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        (await _client.PostJsonAsync($"/api/v1/invoices/{invoiceId}/void", new { reason = "Lập lại" }, s.Token)).StatusCode.Should().Be(HttpStatusCode.OK);

        var charges = await (await _client.GetAsync($"/api/v1/rooms/{(await InvoiceAsync(s, invoiceId)).GetProperty("summary").GetProperty("roomId").GetGuid()}/charges", s.Token))
            .ReadAsync<List<JsonElement>>();
        charges.Single(c => c.GetProperty("id").GetGuid() == charge.GetProperty("id").GetGuid())
            .GetProperty("status").GetString().Should().Be("Cancelled");
    }

    [Fact]
    public async Task ManuallyEditedLine_WarnsAndKeepsManualAmountPlusDifference()
    {
        var s = await ArrangeAsync();
        (await SaveAsync(s, s.Previous, s.Month, 1388)).StatusCode.Should().Be(HttpStatusCode.OK);
        var draft = await GenerateAsync(s, s.Previous);
        var line = Metered(draft);
        (await _client.PutJsonAsync($"/api/v1/invoices/{Id(draft)}/lines/{line.GetProperty("id").GetGuid()}",
            new { amount = 500_000, note = "Giảm cho khách quen" }, s.Token)).StatusCode.Should().Be(HttpStatusCode.OK);
        await FinalizeAsync(s, Id(draft));

        var result = await OkAsync(await AdjustAsync(s, await PeriodicIdAsync(s, 1388), 1338));
        result.GetProperty("warnings").EnumerateArray().Select(w => w.GetProperty("code").GetString()).Should().Contain("LINE_MANUALLY_EDITED");
        var edited = Metered(result.GetProperty("invoice"));
        edited.GetProperty("amount").GetDecimal().Should().Be(500_000 - 50 * Price);
        edited.GetProperty("systemAmount").GetDecimal().Should().Be(88 * Price);
    }
    [Fact]
    public async Task SettledAdjustmentCharge_CannotBeAdjustedAgain_UnsettlingIgnoresCreditLimit()
    {
        var s = await ArrangeAsync();
        var (invoiceId, readingId) = await FinalizedPreviousAsync(s, 2388); // đúng là 1.338 ⇒ thu dư 1.050 kWh = 3.990.000đ > 1 tháng tiền phòng
        await PayAsync(s, invoiceId, Total(await InvoiceAsync(s, invoiceId)));
        var charge = (await OkAsync(await AdjustAsync(s, readingId, 1338, "NextInvoice"))).GetProperty("charge");
        charge.GetProperty("amount").GetDecimal().Should().Be(1050 * Price);
        var chargeId = charge.GetProperty("id").GetGuid();

        (await _client.PostJsonAsync($"/api/v1/room-charges/{chargeId}/settle", new { settledOn = s.Today, method = "Cash" }, s.Token))
            .StatusCode.Should().Be(HttpStatusCode.OK, "chủ trọ trả tay cho người thuê");
        (await (await AdjustAsync(s, readingId, 1340, "NextInvoice")).ReadProblemCodeAsync()).Should().Be("ADJUSTMENT_ALREADY_BILLED");
        (await (await AdjustAsync(s, readingId, 1340, "Immediate")).ReadProblemCodeAsync())
            .Should().Be("ADJUSTMENT_ALREADY_BILLED", "tiền đã trả tay — hủy khoản rồi hoàn lại là trả 2 lần");

        (await _client.DeleteAsync($"/api/v1/room-charges/{chargeId}/settle", s.Token))
            .StatusCode.Should().Be(HttpStatusCode.OK, "khoản điều chỉnh không áp giới hạn bù 1 tháng tiền phòng");
    }

    [Fact]
    public async Task NewValueBelowPeriodStart_ReportsNotMonotonic()
    {
        var s = await ArrangeAsync();
        var (_, readingId) = await FinalizedPreviousAsync(s, 1388);

        (await (await AdjustAsync(s, readingId, 1200)).ReadProblemCodeAsync()).Should().Be("READING_NOT_MONOTONIC", "nhận phòng ở 1.250");
    }

    [Fact]
    public async Task NegativeMoveOutInvoiceAlreadyRefunded_RefusedUntilConfirmationRemoved()
    {
        var s = await ArrangeAsync();
        var (invoiceId, _) = await FinalizedPreviousAsync(s, 1388);
        await PayAsync(s, invoiceId, Total(await InvoiceAsync(s, invoiceId)));
        (await _client.PostJsonAsync($"/api/v1/contracts/{s.ContractId}/liquidation/start",
            new { actualEndDate = s.Month, reason = "MutualAgreement", finalPeriodCharge = "Daily" }, s.Token)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        var moveOut = await _client.PostJsonAsync($"/api/v1/contracts/{s.ContractId}/final-invoice",
            new { finalReadings = new[] { new { meterId = s.MeterId, value = 1400m } } }, s.Token);
        var moveOutId = Id(await moveOut.ReadAsync<JsonElement>());
        var roomId = (await InvoiceAsync(s, moveOutId)).GetProperty("summary").GetProperty("roomId").GetGuid();
        (await _client.PostJsonAsync($"/api/v1/rooms/{roomId}/charges", new
        {
            kind = "Credit", description = "Giảm do cúp nước", amount = 1_000_000, incurredOn = s.Month, reason = "Cúp nước 3 ngày"
        }, s.Token)).StatusCode.Should().Be(HttpStatusCode.Created);
        await FinalizeAsync(s, moveOutId);
        Total(await InvoiceAsync(s, moveOutId)).Should().BeNegative();
        (await _client.PostJsonAsync($"/api/v1/invoices/{moveOutId}/refund", new { refundedOn = s.Today, method = "Cash" }, s.Token))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        var finalReading = Metered(await InvoiceAsync(s, moveOutId)).GetProperty("segments")[0].GetProperty("endReadingId").GetGuid();

        (await (await AdjustAsync(s, finalReading, 1395)).ReadProblemCodeAsync()).Should().Be("REFUND_CONFIRMED");
        (await _client.DeleteAsync($"/api/v1/invoices/{moveOutId}/refund", s.Token)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await OkAsync(await AdjustAsync(s, finalReading, 1395))).GetProperty("handling").GetString().Should().Be("Recalculated");
    }

    [Fact]
    public async Task ManualAmountPlusDifferenceBelowZero_SetToZeroWithWarning()
    {
        var s = await ArrangeAsync();
        (await SaveAsync(s, s.Previous, s.Month, 1388)).StatusCode.Should().Be(HttpStatusCode.OK);
        var draft = await GenerateAsync(s, s.Previous);
        (await _client.PutJsonAsync($"/api/v1/invoices/{Id(draft)}/lines/{Metered(draft).GetProperty("id").GetGuid()}",
            new { amount = 100_000, note = "Giảm cho khách quen" }, s.Token)).StatusCode.Should().Be(HttpStatusCode.OK);
        await FinalizeAsync(s, Id(draft));

        var result = await OkAsync(await AdjustAsync(s, await PeriodicIdAsync(s, 1388), 1338));

        result.GetProperty("warnings").EnumerateArray().Select(w => w.GetProperty("code").GetString()).Should().Contain("MANUAL_AMOUNT_CLAMPED");
        Metered(result.GetProperty("invoice")).GetProperty("amount").GetDecimal().Should().Be(0);
    }

    [Fact]
    public async Task RecalculationWouldMakeMonthlyInvoiceNegative_Rejected_PushToNextInvoiceAllowedEvenUnpaid()
    {
        var s = await ArrangeAsync();
        (await SaveAsync(s, s.Previous, s.Month, 1388)).StatusCode.Should().Be(HttpStatusCode.OK);
        var draft = await GenerateAsync(s, s.Previous);
        var rentLine = draft.GetProperty("lines").EnumerateArray().Single(l => l.GetProperty("type").GetString() == "Rent").GetProperty("id").GetGuid();
        (await _client.PutJsonAsync($"/api/v1/invoices/{Id(draft)}/lines/{rentLine}", new { amount = 3_000_000, note = "Sửa nhà" }, s.Token))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        (await _client.PostJsonAsync($"/api/v1/invoices/{Id(draft)}/manual-lines",
            new { type = "ManualDiscount", description = "Miễn tiền tháng sửa nhà", amount = 3_500_000, note = "Sửa nhà" }, s.Token))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        await FinalizeAsync(s, Id(draft));
        var before = Total(await InvoiceAsync(s, Id(draft)));
        before.Should().BeLessThan(50 * Price, "tiền phòng 3.000.000 + điện 524.400 − giảm 3.500.000 (làm tròn)");
        var readingId = await PeriodicIdAsync(s, 1388);

        (await (await AdjustAsync(s, readingId, 1338)).ReadProblemCodeAsync()).Should().Be("ADJUSTMENT_NEGATIVE_TOTAL");
        Total(await InvoiceAsync(s, Id(draft))).Should().Be(before, "không đổi gì");
        (await PeriodicIdAsync(s, 1388)).Should().Be(readingId);

        var pushed = await OkAsync(await AdjustAsync(s, readingId, 1338, "NextInvoice"));
        pushed.GetProperty("handling").GetString().Should().Be("NextInvoice");
        pushed.GetProperty("charge").GetProperty("amount").GetDecimal().Should().Be(50 * Price);
        Total(pushed.GetProperty("invoice")).Should().Be(before);
    }
}
