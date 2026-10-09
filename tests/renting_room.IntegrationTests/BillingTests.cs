using System.Net;
using System.Text.Json;
using renting_room.IntegrationTests.Infrastructure;

namespace renting_room.IntegrationTests;

/// <summary>M06 đợt 2 + M07 đợt 1 + M08 đợt 1: ghi chỉ số → phiếu nháp → sửa tay / phụ thu → chốt → thu tiền → đảo / hủy.</summary>
[Collection(ApiCollection.Name)]
public sealed class BillingTests(ApiFactory factory)
{
    private readonly HttpClient _client = factory.CreateClient();

    private sealed record Setup(string Token, Guid PropertyId, Guid RoomId, Guid ContractId, Guid MeterId, Guid ElectricityId, DateOnly Start);

    /// <summary>Kỳ thu ngày 1, bắt đầu ngày 1 của 2 tháng trước ⇒ các kỳ là tháng tròn, không phụ thuộc ngày chạy test.</summary>
    private async Task<Setup> ArrangeAsync(string chargeMode)
    {
        var owner = await _client.CreateActiveOwnerAsync();
        var token = owner.Tokens.AccessToken;
        var today = TestData.Today(factory);
        var start = new DateOnly(today.Year, today.Month, 1).AddMonths(-2);
        var propertyId = await _client.CreatePropertyAsync(token, anchorDay: 1, chargeMode: chargeMode);
        var roomId = await _client.CreateRoomAsync(token, propertyId);
        var electricity = await _client.FeeIdAsync(token, propertyId, "Điện");
        await _client.PostJsonAsync($"/api/v1/fee-types/{electricity}/prices", new { effectiveFrom = start.AddDays(-30), unitPrice = 3500 }, token);
        var water = await (await _client.PostJsonAsync($"/api/v1/properties/{propertyId}/fee-types", new
        {
            name = "Nước theo người", group = "Service", chargeBasis = "PerOccupant", unit = "người", autoAttach = false,
            initialPrice = new { effectiveFrom = start.AddDays(-30), unitPrice = 20000 }
        }, token)).ReadIdAsync();
        var meter = await _client.InstallMeterAsync(token, roomId, electricity, start.AddDays(-1), 100);
        var renter = await _client.CreateRenterAsync(token);

        var created = await _client.PostJsonAsync("/api/v1/contracts", new
        {
            roomId,
            contract = new
            {
                representativeRenterId = renter, startDate = start, monthlyRent = 3_000_000, depositAmount = 0,
                occupants = new[] { new { renterId = renter } },
                fees = new[] { new { feeTypeId = water } }
            }
        }, token);
        created.StatusCode.Should().Be(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        var contractId = await created.ReadIdAsync();
        (await _client.PostJsonAsync($"/api/v1/contracts/{contractId}/activate",
            new { handoverReadings = new[] { new { meterId = meter, value = (decimal?)null } } }, token)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        return new Setup(token, propertyId, roomId, contractId, meter, electricity, start);
    }

    private async Task<JsonElement> GenerateAsync(Setup s, DateOnly month) =>
        await (await _client.PostJsonAsync("/api/v1/invoices/generate", new { propertyId = s.PropertyId, billingMonth = $"{month:yyyy-MM}" }, s.Token))
            .ReadAsync<JsonElement>();

    private async Task<JsonElement> InvoiceForAsync(Setup s, DateOnly month)
    {
        var page = await (await _client.GetAsync($"/api/v1/invoices?contractId={s.ContractId}&billingMonth={month:yyyy-MM}", s.Token)).ReadAsync<JsonElement>();
        var id = page.GetProperty("items")[0].GetProperty("id").GetGuid();
        return await (await _client.GetAsync($"/api/v1/invoices/{id}", s.Token)).ReadAsync<JsonElement>();
    }

    private static Guid Id(JsonElement invoice) => invoice.GetProperty("summary").GetProperty("id").GetGuid();

    private static decimal Total(JsonElement invoice) => invoice.GetProperty("summary").GetProperty("totalAmount").GetDecimal();

    private Task<HttpResponseMessage> SaveReadingAsync(Setup s, DateOnly closingPeriodStart, decimal value, Guid? meterId = null) =>
        _client.PutJsonAsync($"/api/v1/properties/{s.PropertyId}/meter-readings", new
        {
            readings = new[]
            {
                new { meterId = meterId ?? s.MeterId, contractId = s.ContractId, closingPeriodStart, readingDate = closingPeriodStart.AddMonths(1).AddDays(-1), value }
            }
        }, s.Token);

    [Fact]
    public async Task Postpaid_ReadingSheet_Draft_ManualEdit_Finalize_Pay_Void()
    {
        var s = await ArrangeAsync("Postpaid");
        var month = s.Start;

        var generated = await GenerateAsync(s, month);
        generated.GetProperty("created").GetInt32().Should().Be(1);
        generated.GetProperty("withIssues").GetInt32().Should().Be(1, "chưa có chỉ số cuối kỳ");
        var draft = await InvoiceForAsync(s, month);
        draft.GetProperty("issues").EnumerateArray().Select(i => i.GetProperty("code").GetString()).Should().Contain("MISSING_READING");
        (await (await _client.PostJsonAsync($"/api/v1/invoices/{Id(draft)}/finalize", null, s.Token)).ReadProblemCodeAsync()).Should().Be("INVOICE_HAS_ISSUES");

        // Lưới ghi chỉ số: chỉ số cũ = chỉ số nhận phòng 100.
        var sheet = await (await _client.GetAsync($"/api/v1/properties/{s.PropertyId}/meter-reading-sheet?billingMonth={month:yyyy-MM}", s.Token)).ReadAsync<JsonElement>();
        var row = sheet.GetProperty("rows").EnumerateArray().Single();
        row.GetProperty("previous").GetProperty("value").GetDecimal().Should().Be(100);
        row.GetProperty("current").ValueKind.Should().Be(JsonValueKind.Null);
        (await SaveReadingAsync(s, month, 99)).StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity, "chỉ số mới nhỏ hơn chỉ số cũ");
        (await SaveReadingAsync(s, month, 188)).StatusCode.Should().Be(HttpStatusCode.OK);

        // Tính lại: Tiền phòng 3.000.000 + Điện 88 × 3.500 + Nước 1 người × 20.000.
        await _client.PostJsonAsync("/api/v1/invoices/recalculate", new { propertyId = s.PropertyId, billingMonth = $"{month:yyyy-MM}" }, s.Token);
        draft = await InvoiceForAsync(s, month);
        draft.GetProperty("issues").EnumerateArray().Should().BeEmpty();
        draft.GetProperty("lines").EnumerateArray().Select(l => (l.GetProperty("type").GetString(), l.GetProperty("amount").GetDecimal()))
            .Should().Equal(("Rent", 3_000_000m), ("Metered", 308_000m), ("Service", 20_000m));
        Total(draft).Should().Be(3_328_000);

        // Sửa tay tiền phòng + phụ thu sửa khóa.
        var rentLine = draft.GetProperty("lines")[0].GetProperty("id").GetGuid();
        var edited = await (await _client.PutJsonAsync($"/api/v1/invoices/{Id(draft)}/lines/{rentLine}",
            new { amount = 2_800_000, note = "Giảm do sửa nhà" }, s.Token)).ReadAsync<JsonElement>();
        edited.GetProperty("lines")[0].GetProperty("isManuallyEdited").GetBoolean().Should().BeTrue();
        edited.GetProperty("lines")[0].GetProperty("systemAmount").GetDecimal().Should().Be(3_000_000);
        var surcharge = await _client.PostJsonAsync($"/api/v1/invoices/{Id(draft)}/manual-lines",
            new { type = "Surcharge", description = "Thay khóa cửa", amount = 250_000, note = "Người thuê làm hỏng khóa" }, s.Token);
        Total(await surcharge.ReadAsync<JsonElement>()).Should().Be(3_378_000);

        // Tính lại vẫn giữ ô sửa tay + phụ thu.
        await _client.PostJsonAsync("/api/v1/invoices/recalculate", new { invoiceIds = new[] { Id(draft) } }, s.Token);
        Total(await InvoiceForAsync(s, month)).Should().Be(3_378_000);

        var finalized = await (await _client.PostJsonAsync($"/api/v1/invoices/{Id(draft)}/finalize", null, s.Token)).ReadAsync<JsonElement>();
        finalized.GetProperty("summary").GetProperty("invoiceNo").GetString().Should().StartWith($"PB{TestData.Today(factory).Year}-");
        finalized.GetProperty("summary").GetProperty("status").GetString().Should().Be("Finalized");

        // Đã chốt ⇒ khóa chỉ số, bảng giá trước ngày cuối kỳ, không sửa phiếu.
        var periodic = row.GetProperty("meterId").GetGuid();
        var readings = await (await _client.GetAsync($"/api/v1/meters/{periodic}/readings", s.Token)).ReadAsync<List<JsonElement>>();
        (await (await _client.PutJsonAsync($"/api/v1/meter-readings/{readings[0].GetProperty("id").GetGuid()}", new { value = 190 }, s.Token))
            .ReadProblemCodeAsync()).Should().Be("READING_LOCKED");
        (await (await _client.PostJsonAsync($"/api/v1/fee-types/{s.ElectricityId}/prices", new { effectiveFrom = month.AddDays(3), unitPrice = 3600 }, s.Token))
            .ReadProblemCodeAsync()).Should().Be("FEE_PRICE_LOCKED");
        (await (await _client.PutJsonAsync($"/api/v1/invoices/{Id(draft)}/lines/{rentLine}", new { amount = 1 , note = "x" }, s.Token))
            .ReadProblemCodeAsync()).Should().Be("INVOICE_NOT_DRAFT");

        // Thu 1 phần bằng tay; thu vượt nợ bị chặn; phòng hiện "còn nợ".
        var paid = await _client.PostJsonAsync($"/api/v1/contracts/{s.ContractId}/payments",
            new { amount = 1_000_000, method = "Cash", paidAt = TestData.Today(factory), invoiceId = Id(draft) }, s.Token);
        paid.StatusCode.Should().Be(HttpStatusCode.Created, await paid.Content.ReadAsStringAsync());
        var payment = await paid.ReadAsync<JsonElement>();
        payment.GetProperty("receiptNo").GetString().Should().StartWith("PT");
        (await (await _client.PostJsonAsync($"/api/v1/contracts/{s.ContractId}/payments",
                new { amount = 5_000_000, method = "Cash", paidAt = TestData.Today(factory), invoiceId = Id(draft) }, s.Token))
            .ReadProblemCodeAsync()).Should().Be("PAYMENT_EXCEEDS_DEBT");
        (await InvoiceForAsync(s, month)).GetProperty("summary").GetProperty("paymentStatus").GetString().Should().BeOneOf("PartiallyPaid", "Overdue");
        var room = await (await _client.GetAsync($"/api/v1/rooms/{s.RoomId}", s.Token)).ReadAsync<JsonElement>();
        room.GetProperty("outstandingAmount").GetDecimal().Should().Be(2_378_000);

        // Hủy: đã thu tiền ⇒ chặn; có phiếu kỳ sau ⇒ chặn (LIFO); đảo phiếu thu + xóa nháp kỳ sau ⇒ hủy được.
        (await (await _client.PostJsonAsync($"/api/v1/invoices/{Id(draft)}/void", new { reason = "Sai" }, s.Token)).ReadProblemCodeAsync())
            .Should().Be("INVOICE_HAS_PAYMENTS");
        (await GenerateAsync(s, month.AddMonths(1))).GetProperty("created").GetInt32().Should().Be(1, "kỳ trước đã lập phiếu");
        (await _client.PostJsonAsync($"/api/v1/payments/{payment.GetProperty("id").GetGuid()}/reverse", new { reason = "Nhập nhầm" }, s.Token))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        (await (await _client.PostJsonAsync($"/api/v1/invoices/{Id(draft)}/void", new { reason = "Sai" }, s.Token)).ReadProblemCodeAsync())
            .Should().Be("NOT_LATEST_INVOICE");
        var next = await InvoiceForAsync(s, month.AddMonths(1));
        (await _client.DeleteAsync($"/api/v1/invoices/{Id(next)}", s.Token)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await _client.PostJsonAsync($"/api/v1/invoices/{Id(draft)}/void", new { reason = "Sai" }, s.Token)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await _client.PutJsonAsync($"/api/v1/meter-readings/{readings[0].GetProperty("id").GetGuid()}", new { value = 190 }, s.Token))
            .StatusCode.Should().Be(HttpStatusCode.OK, "phiếu đã hủy ⇒ chỉ số được mở khóa");
    }

    [Fact]
    public async Task Prepaid_SecondInvoice_MeterReplacedMidPeriod_NewPriceForWholePeriod()
    {
        var s = await ArrangeAsync("Prepaid");
        var previousMonth = s.Start.AddMonths(1);

        // Thay công tơ ngày 11 của kỳ đầu: cũ 100 → 150, mới 0 → 30. Giá đổi giữa kỳ ⇒ cả kỳ tính giá mới.
        var replaced = await _client.PostJsonAsync($"/api/v1/meters/{s.MeterId}/replace",
            new { date = s.Start.AddDays(10), oldFinalValue = 150, newSerialNo = "E-2", newInitialValue = 0 }, s.Token);
        var newMeter = await replaced.ReadIdAsync();
        await _client.PostJsonAsync($"/api/v1/fee-types/{s.ElectricityId}/prices", new { effectiveFrom = s.Start.AddDays(15), unitPrice = 4000 }, s.Token);
        (await SaveReadingAsync(s, s.Start, 30, newMeter)).StatusCode.Should().Be(HttpStatusCode.OK);

        // BL-BR-21: lập lần lượt — chưa lập kỳ đầu thì tháng 2 bị bỏ qua. Prepaid ⇒ phiếu tháng 2 thu điện nước của kỳ đầu.
        (await GenerateAsync(s, previousMonth)).GetProperty("skipped")[0].GetProperty("reason").GetString().Should().Be("PREVIOUS_PERIOD_NOT_BILLED");
        (await GenerateAsync(s, s.Start)).GetProperty("created").GetInt32().Should().Be(1);
        (await GenerateAsync(s, previousMonth)).GetProperty("created").GetInt32().Should().Be(1);
        var invoice = await InvoiceForAsync(s, previousMonth);
        var metered = invoice.GetProperty("lines").EnumerateArray().Single(l => l.GetProperty("type").GetString() == "Metered");
        metered.GetProperty("quantity").GetDecimal().Should().Be(50 + 30);
        metered.GetProperty("unitPrice").GetDecimal().Should().Be(4000);
        metered.GetProperty("segments").EnumerateArray().Should().HaveCount(2);

        var skipped = await GenerateAsync(s, s.Start);
        skipped.GetProperty("skipped")[0].GetProperty("reason").GetString().Should().Be("EXISTS");

        // CT-BR-11: còn phiếu kỳ sau ngày trả phòng ⇒ không bắt đầu thanh lý.
        (await (await _client.PostJsonAsync($"/api/v1/contracts/{s.ContractId}/liquidation/start",
                new { actualEndDate = s.Start.AddDays(20), reason = "MutualAgreement" }, s.Token))
            .ReadProblemCodeAsync()).Should().Be("INVOICE_AFTER_END_DATE");
    }

    private async Task<JsonElement> FinalizeAsync(Setup s, Guid invoiceId)
    {
        var response = await _client.PostJsonAsync($"/api/v1/invoices/{invoiceId}/finalize", null, s.Token);
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return await response.ReadAsync<JsonElement>();
    }

    [Fact]
    public async Task MoveOutMidPeriod_FinalInvoiceChargesRemainingElectricity_WriteOffDebt_EndsContract()
    {
        var s = await ArrangeAsync("Prepaid");
        var secondMonth = s.Start.AddMonths(1);

        // Phiếu tháng 1 (không điện — trả trước), tháng 2 (tiền phòng tháng 2 + điện tháng 1), đều đã chốt.
        await GenerateAsync(s, s.Start);
        await FinalizeAsync(s, Id(await InvoiceForAsync(s, s.Start)));
        (await SaveReadingAsync(s, s.Start, 150)).StatusCode.Should().Be(HttpStatusCode.OK);
        await GenerateAsync(s, secondMonth);
        await FinalizeAsync(s, Id(await InvoiceForAsync(s, secondMonth)));

        // Ở nửa tháng 2 rồi đi: tiền phòng tháng 2 đã thu trọn (không hoàn — để sau), điện nửa tháng 2 chưa ai thu.
        var moveOut = secondMonth.AddDays(14);
        (await _client.PostJsonAsync($"/api/v1/contracts/{s.ContractId}/liquidation/start",
            new { actualEndDate = moveOut, reason = "LesseeUnilateral" }, s.Token)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        var created = await _client.PostJsonAsync($"/api/v1/contracts/{s.ContractId}/final-invoice",
            new { finalReadings = new[] { new { meterId = s.MeterId, value = (decimal?)170 } } }, s.Token);
        created.StatusCode.Should().Be(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        var final = await created.ReadAsync<JsonElement>();
        final.GetProperty("lines").EnumerateArray().Select(l => (l.GetProperty("type").GetString(), l.GetProperty("quantity").GetDecimal()))
            .Should().Equal(("Metered", 20m)); // 150 → 170; tiền phòng, nước đã thu ở phiếu tháng 2
        (await (await _client.PostJsonAsync($"/api/v1/contracts/{s.ContractId}/final-invoice", new { finalReadings = (object?)null }, s.Token))
            .ReadProblemCodeAsync()).Should().Be("FINAL_INVOICE_EXISTS");

        // Phụ thu phạt báo trễ trên phiếu quyết toán rồi chốt.
        await _client.PostJsonAsync($"/api/v1/invoices/{Id(final)}/manual-lines",
            new { type = "Surcharge", description = "Phạt báo trả phòng muộn", amount = 500_000, note = "Theo điều khoản HĐ" }, s.Token);
        Total(await FinalizeAsync(s, Id(final))).Should().Be(20 * 3500 + 500_000);

        // Còn nợ ⇒ cảnh báo; người thuê trốn ⇒ bỏ nợ (không tính doanh thu).
        var warn = await _client.PostJsonAsync($"/api/v1/contracts/{s.ContractId}/liquidation/complete", new { settlement = (string?)null }, s.Token);
        (await warn.ReadProblemCodeAsync()).Should().Be("CONTRACT_HAS_DEBT");
        (await _client.PostJsonAsync($"/api/v1/contracts/{s.ContractId}/liquidation/complete",
            new { settlement = "WriteOff", reason = "Người thuê bỏ đi không trả" }, s.Token)).StatusCode.Should().Be(HttpStatusCode.NoContent);

        var contract = await (await _client.GetAsync($"/api/v1/contracts/{s.ContractId}", s.Token)).ReadAsync<JsonElement>();
        contract.GetProperty("status").GetString().Should().Be("Ended");
        var page = await (await _client.GetAsync($"/api/v1/invoices?contractId={s.ContractId}", s.Token)).ReadAsync<JsonElement>();
        page.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("paymentStatus").GetString()).Should().Contain("WrittenOff");
        var payments = await (await _client.GetAsync($"/api/v1/payments?contractId={s.ContractId}", s.Token)).ReadAsync<List<JsonElement>>();
        payments.Should().Contain(p => p.GetProperty("kind").GetString() == "WriteOff");
        (await (await _client.PostJsonAsync($"/api/v1/contracts/{s.ContractId}/payments",
                new { amount = 1000, method = "Cash", paidAt = TestData.Today(factory) }, s.Token))
            .ReadProblemCodeAsync()).Should().Be("CONTRACT_NOT_BILLABLE");
    }

    [Fact]
    public async Task MoveOutEarly_OverpaidRentWarned_RefundLineMakesTotalNegative_RefundConfirmedBeforeCompletion()
    {
        var s = await ArrangeAsync("Prepaid");
        var secondMonth = s.Start.AddMonths(1);
        await GenerateAsync(s, s.Start);
        await FinalizeAsync(s, Id(await InvoiceForAsync(s, s.Start)));
        (await SaveReadingAsync(s, s.Start, 150)).StatusCode.Should().Be(HttpStatusCode.OK);
        await GenerateAsync(s, secondMonth);
        await FinalizeAsync(s, Id(await InvoiceForAsync(s, secondMonth)));

        // Đã đóng trọn tiền phòng tháng 2 nhưng ở 15 ngày ⇒ phiếu quyết toán nhắc phần thu thừa, không tự hoàn.
        var moveOut = secondMonth.AddDays(14);
        (await _client.PostJsonAsync($"/api/v1/contracts/{s.ContractId}/liquidation/start",
            new { actualEndDate = moveOut, reason = "MutualAgreement" }, s.Token)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        var final = await (await _client.PostJsonAsync($"/api/v1/contracts/{s.ContractId}/final-invoice",
            new { finalReadings = new[] { new { meterId = s.MeterId, value = (decimal?)170 } } }, s.Token)).ReadAsync<JsonElement>();
        final.GetProperty("issues").EnumerateArray().Select(i => i.GetProperty("code").GetString()).Should().Contain("RENT_OVERPAID");

        // Hoàn trả lớn hơn phần thu ⇒ tổng âm = chủ trọ phải trả lại.
        var refund = await _client.PostJsonAsync($"/api/v1/invoices/{Id(final)}/manual-lines",
            new { type = "Refund", description = "Hoàn tiền phòng 15 ngày chưa ở", amount = 1_000_000, note = "Trả phòng sớm theo thỏa thuận" }, s.Token);
        refund.StatusCode.Should().Be(HttpStatusCode.OK, await refund.Content.ReadAsStringAsync());
        var finalized = await FinalizeAsync(s, Id(final));
        Total(finalized).Should().Be(20 * 3500 - 1_000_000);
        finalized.GetProperty("summary").GetProperty("paymentStatus").GetString().Should().Be("RefundPending");
        finalized.GetProperty("summary").GetProperty("refundDue").GetDecimal().Should().Be(930_000);
        finalized.GetProperty("summary").GetProperty("outstanding").GetDecimal().Should().Be(0);

        (await (await _client.PostJsonAsync($"/api/v1/contracts/{s.ContractId}/liquidation/complete", new { settlement = (string?)null }, s.Token))
            .ReadProblemCodeAsync()).Should().Be("REFUND_PENDING");

        var today = TestData.Today(factory);
        var confirmed = await _client.PostJsonAsync($"/api/v1/invoices/{Id(final)}/refund",
            new { refundedOn = today, method = "Cash", note = "Trả tiền mặt khi bàn giao" }, s.Token);
        confirmed.StatusCode.Should().Be(HttpStatusCode.OK, await confirmed.Content.ReadAsStringAsync());
        (await confirmed.ReadAsync<JsonElement>()).GetProperty("summary").GetProperty("paymentStatus").GetString().Should().Be("Refunded");

        // Phiếu tháng 1, 2 chưa thu ⇒ còn nợ; phiếu hoàn trả không bù trừ nợ (E2 để sau) ⇒ chọn "Đã thu toàn bộ".
        (await (await _client.PostJsonAsync($"/api/v1/contracts/{s.ContractId}/liquidation/complete", new { settlement = (string?)null }, s.Token))
            .ReadProblemCodeAsync()).Should().Be("CONTRACT_HAS_DEBT");
        (await _client.PostJsonAsync($"/api/v1/contracts/{s.ContractId}/liquidation/complete", new { settlement = "CollectAll" }, s.Token))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task BulkManualLine_AddsSameSurchargeToEachDraft_ReportsRoomsWithoutDraft()
    {
        var s = await ArrangeAsync("Postpaid");
        var emptyRoom = await _client.CreateRoomAsync(s.Token, s.PropertyId);
        await SaveReadingAsync(s, s.Start, 150);
        await GenerateAsync(s, s.Start);
        var before = Total(await InvoiceForAsync(s, s.Start));

        var response = await _client.PostJsonAsync("/api/v1/invoices/manual-lines", new
        {
            propertyId = s.PropertyId, billingMonth = $"{s.Start:yyyy-MM}", roomIds = new[] { s.RoomId, emptyRoom },
            type = "Surcharge", description = "Sơn lại hành lang", amount = 50_000, note = "Thu chung cả khu theo thông báo"
        }, s.Token);
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var result = await response.ReadAsync<JsonElement>();

        result.GetProperty("added").GetInt32().Should().Be(1);
        result.GetProperty("results").EnumerateArray()
            .Select(r => (r.GetProperty("roomId").GetGuid(), r.GetProperty("success").GetBoolean(), r.GetProperty("errorCode").GetString()))
            .Should().BeEquivalentTo([(s.RoomId, true, (string?)null), (emptyRoom, false, "NO_DRAFT_INVOICE")]);
        Total(await InvoiceForAsync(s, s.Start)).Should().Be(before + 50_000);

        (await (await _client.PostJsonAsync("/api/v1/invoices/manual-lines", new
            {
                propertyId = s.PropertyId, billingMonth = $"{s.Start:yyyy-MM}", type = "Rent", description = "x", amount = 1, note = "x"
            }, s.Token))
            .ReadProblemCodeAsync()).Should().Be("VALIDATION_FAILED");
    }

    [Fact]
    public async Task TieredElectricityPrice_ChargesEachSliceAtItsRate()
    {
        var s = await ArrangeAsync("Postpaid");
        var tiered = await _client.PostJsonAsync($"/api/v1/fee-types/{s.ElectricityId}/prices", new
        {
            effectiveFrom = s.Start,
            tiers = new object[] { new { upTo = 50, price = 2000 }, new { upTo = (int?)null, price = 3000 } }
        }, s.Token);
        tiered.StatusCode.Should().Be(HttpStatusCode.Created, await tiered.Content.ReadAsStringAsync());

        await SaveReadingAsync(s, s.Start, 188);
        await GenerateAsync(s, s.Start);
        var metered = (await InvoiceForAsync(s, s.Start)).GetProperty("lines").EnumerateArray()
            .Single(l => l.GetProperty("type").GetString() == "Metered");
        metered.GetProperty("amount").GetDecimal().Should().Be(50 * 2000 + 38 * 3000);
        metered.GetProperty("description").GetString().Should().Be("Điện (giá bậc)");

        var fees = await (await _client.GetAsync($"/api/v1/properties/{s.PropertyId}/fee-types", s.Token)).ReadAsync<List<JsonElement>>();
        var water = fees.Single(f => f.GetProperty("name").GetString() == "Nước theo người").GetProperty("id").GetGuid();
        (await (await _client.PostJsonAsync($"/api/v1/fee-types/{water}/prices", new
            {
                effectiveFrom = s.Start.AddDays(1), tiers = new object[] { new { upTo = (int?)null, price = 20000 } }
            }, s.Token))
            .ReadProblemCodeAsync()).Should().Be("FEE_TIERS_METERED_ONLY");
    }
}
