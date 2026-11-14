using System.Net;
using System.Text.Json;
using renting_room.IntegrationTests.Infrastructure;

namespace renting_room.IntegrationTests;

/// <summary>
/// M08 PM-UC-22 / PM-BR-38 (09/11/2026, 98 H-04): sửa trực tiếp số "Đã thu" của hóa đơn — tăng ⇒ ghi phiếu thu phần chênh; giảm ⇒ lý do bắt buộc,
/// app đảo phiếu thu mới nhất rồi ghi lại phần còn giữ; quản lý khu không giảm được tiền thu trước tháng trước. Kịch bản S1–S5, S7, S8 ở plan 08 §12
/// (S6 — phiếu hoàn — ở <see cref="MeterReadingAdjustmentTests"/>).
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class CollectedAmountEditTests(ApiFactory factory)
{
    private const decimal Rent = 3_000_000;
    private readonly HttpClient _client = factory.CreateClient();

    private sealed record Setup(string Token, Guid PropertyId, Guid ContractId, Guid PreviousInvoiceId, Guid CurrentInvoiceId, DateOnly Start, DateOnly Today);

    /// <summary>Khu thu trước chốt ngày 1, HĐ từ đầu tháng trước ⇒ 2 hóa đơn 3tr đã chốt (tháng trước, tháng này), chưa thu.</summary>
    private async Task<Setup> ArrangeAsync(string? ownerToken = null)
    {
        var token = ownerToken ?? (await _client.CreateActiveOwnerAsync()).Tokens.AccessToken;
        var today = TestData.Today(factory);
        var month = new DateOnly(today.Year, today.Month, 1);
        var start = month.AddMonths(-1);
        var propertyId = await _client.CreatePropertyAsync(token, anchorDay: 1, chargeMode: "Prepaid");
        var roomId = await _client.CreateRoomAsync(token, propertyId);
        var renter = await _client.CreateRenterAsync(token);
        var contractId = await (await _client.PostJsonAsync("/api/v1/contracts", new
        {
            roomId,
            contract = new { representativeRenterId = renter, startDate = start, monthlyRent = Rent, depositAmount = 0, occupants = new[] { new { renterId = renter } } }
        }, token)).ReadIdAsync();
        (await _client.PostJsonAsync($"/api/v1/contracts/{contractId}/activate", null, token)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        var previous = await GenerateAndFinalizeAsync(token, propertyId, contractId, start);
        var current = await GenerateAndFinalizeAsync(token, propertyId, contractId, month);
        return new Setup(token, propertyId, contractId, previous, current, start, today);
    }

    private async Task<Guid> GenerateAndFinalizeAsync(string token, Guid propertyId, Guid contractId, DateOnly month)
    {
        (await _client.PostJsonAsync("/api/v1/invoices/generate", new { propertyId, billingMonth = $"{month:yyyy-MM}" }, token)).StatusCode
            .Should().Be(HttpStatusCode.OK);
        var page = await (await _client.GetAsync($"/api/v1/invoices?contractId={contractId}&billingMonth={month:yyyy-MM}", token)).ReadAsync<JsonElement>();
        var id = page.GetProperty("items")[0].GetProperty("id").GetGuid();
        var finalized = await _client.FinalizeInvoiceAsync(id, token);
        finalized.StatusCode.Should().Be(HttpStatusCode.OK, await finalized.Content.ReadAsStringAsync());
        return id;
    }

    private Task<HttpResponseMessage> EditAsync(string token, Guid invoiceId, decimal amount, decimal expected, string? reason = null,
        DateOnly? paidAt = null, string? method = null) =>
        _client.PutJsonAsync($"/api/v1/invoices/{invoiceId}/collected", new { amount, expectedAmount = expected, reason, paidAt, method }, token);

    private static async Task<JsonElement> OkAsync(HttpResponseMessage response)
    {
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return await response.ReadAsync<JsonElement>();
    }

    private async Task PayAsync(Setup s, decimal amount, DateOnly paidAt, Guid? invoiceId, string method = "Cash") =>
        (await _client.PostJsonAsync($"/api/v1/contracts/{s.ContractId}/payments", new { amount, method, paidAt, invoiceId }, s.Token))
        .StatusCode.Should().Be(HttpStatusCode.Created);

    private async Task<JsonElement> SummaryAsync(string token, Guid invoiceId) =>
        (await (await _client.GetAsync($"/api/v1/invoices/{invoiceId}", token)).ReadAsync<JsonElement>()).GetProperty("summary");

    private async Task<List<JsonElement>> PaymentsAsync(string token, Guid invoiceId) =>
        await (await _client.GetAsync($"/api/v1/payments?invoiceId={invoiceId}", token)).ReadAsync<List<JsonElement>>();

    private static IEnumerable<JsonElement> Active(IEnumerable<JsonElement> payments) =>
        payments.Where(p => p.GetProperty("status").GetString() == "Recorded");

    private static decimal Collected(JsonElement summary) => summary.GetProperty("collectedAmount").GetDecimal();

    [Fact]
    public async Task S1_Increase_RecordsReceiptForTheDifference_WithChosenDateAndMethod()
    {
        var s = await ArrangeAsync();
        var yesterday = s.Today.AddDays(-1);

        var first = await OkAsync(await EditAsync(s.Token, s.CurrentInvoiceId, 2_000_000, 0, paidAt: yesterday, method: "BankTransfer"));

        Collected(first.GetProperty("summary")).Should().Be(2_000_000);
        first.GetProperty("summary").GetProperty("paymentStatus").GetString().Should().Be("PartiallyPaid");
        var receipt = first.GetProperty("payments").EnumerateArray().Single();
        receipt.GetProperty("kind").GetString().Should().Be("Receipt");
        receipt.GetProperty("amount").GetDecimal().Should().Be(2_000_000);
        receipt.GetProperty("paidAt").GetDateTime().Should().Be(yesterday.ToDateTime(TimeOnly.MinValue));
        receipt.GetProperty("method").GetString().Should().Be("BankTransfer");

        var second = await OkAsync(await EditAsync(s.Token, s.CurrentInvoiceId, Rent, 2_000_000));
        second.GetProperty("summary").GetProperty("paymentStatus").GetString().Should().Be("Paid");
        second.GetProperty("payments").EnumerateArray().Single().GetProperty("method").GetString().Should().Be("Cash", "mặc định tiền mặt");
        Active(await PaymentsAsync(s.Token, s.CurrentInvoiceId)).Select(p => p.GetProperty("amount").GetDecimal()).Should().BeEquivalentTo([2_000_000m, 1_000_000m]);
    }

    [Fact]
    public async Task S2_Decrease_ReversesAndRerecords_SameDateAndMethod_ReasonRequired()
    {
        var s = await ArrangeAsync();
        var paidAt = s.Today.AddDays(-2);
        await PayAsync(s, Rent, paidAt, s.CurrentInvoiceId, "BankTransfer");

        (await EditAsync(s.Token, s.CurrentInvoiceId, 2_800_000, Rent)).StatusCode.Should().Be(HttpStatusCode.BadRequest, "giảm phải có lý do");

        var result = await OkAsync(await EditAsync(s.Token, s.CurrentInvoiceId, 2_800_000, Rent, "Nhập nhầm"));

        Collected(result.GetProperty("summary")).Should().Be(2_800_000);
        var payments = await PaymentsAsync(s.Token, s.CurrentInvoiceId);
        var reversed = payments.Single(p => p.GetProperty("status").GetString() == "Reversed");
        reversed.GetProperty("amount").GetDecimal().Should().Be(Rent);
        reversed.GetProperty("reverseReason").GetString().Should().Be("Sửa đã thu: Nhập nhầm");
        var kept = Active(payments).Single();
        kept.GetProperty("amount").GetDecimal().Should().Be(2_800_000);
        kept.GetProperty("paidAt").GetDateTime().Should().Be(paidAt.ToDateTime(TimeOnly.MinValue));
        kept.GetProperty("method").GetString().Should().Be("BankTransfer");
        kept.GetProperty("receiptNo").GetString().Should().NotBe(reversed.GetProperty("receiptNo").GetString());
    }

    [Fact]
    public async Task S3_Decrease_AcrossTwoReceipts_NewestFirst()
    {
        var s = await ArrangeAsync();
        var older = s.Today.AddDays(-5);
        await PayAsync(s, 2_000_000, older, s.CurrentInvoiceId);
        await PayAsync(s, 1_000_000, s.Today, s.CurrentInvoiceId);

        await OkAsync(await EditAsync(s.Token, s.CurrentInvoiceId, 1_500_000, Rent, "Thu nhầm phòng"));

        var active = Active(await PaymentsAsync(s.Token, s.CurrentInvoiceId)).Single();
        active.GetProperty("amount").GetDecimal().Should().Be(1_500_000);
        active.GetProperty("paidAt").GetDateTime().Should().Be(older.ToDateTime(TimeOnly.MinValue), "phiếu mới nhất bị đảo hẳn, phiếu cũ giữ ngày thu");
        Collected(await SummaryAsync(s.Token, s.CurrentInvoiceId)).Should().Be(1_500_000);
    }

    [Fact]
    public async Task S4_Decrease_ContractLevelReceipt_KeepsOtherInvoiceAllocation()
    {
        var s = await ArrangeAsync();
        await PayAsync(s, 5_000_000, s.Today, invoiceId: null); // cũ nhất trước: tháng trước 3tr + tháng này 2tr

        await OkAsync(await EditAsync(s.Token, s.CurrentInvoiceId, 1_000_000, 2_000_000, "Ghi nhầm số"));

        Collected(await SummaryAsync(s.Token, s.PreviousInvoiceId)).Should().Be(Rent);
        Collected(await SummaryAsync(s.Token, s.CurrentInvoiceId)).Should().Be(1_000_000);
        var receipt = Active(await PaymentsAsync(s.Token, s.CurrentInvoiceId)).Single();
        receipt.GetProperty("amount").GetDecimal().Should().Be(4_000_000);
        receipt.GetProperty("allocations").EnumerateArray().Select(a => (a.GetProperty("invoiceId").GetGuid(), a.GetProperty("amount").GetDecimal()))
            .Should().BeEquivalentTo([(s.PreviousInvoiceId, Rent), (s.CurrentInvoiceId, 1_000_000m)]);
    }

    [Fact]
    public async Task S5_Errors_StaleExpected_OverTotal_Unchanged()
    {
        var s = await ArrangeAsync();
        await PayAsync(s, 1_000_000, s.Today, s.CurrentInvoiceId);

        (await (await EditAsync(s.Token, s.CurrentInvoiceId, 2_000_000, 0)).ReadProblemCodeAsync()).Should().Be("COLLECTED_AMOUNT_CHANGED");
        (await (await EditAsync(s.Token, s.CurrentInvoiceId, Rent + 1_000, 1_000_000)).ReadProblemCodeAsync()).Should().Be("PAYMENT_EXCEEDS_DEBT");
        (await (await EditAsync(s.Token, s.CurrentInvoiceId, 1_000_000, 1_000_000)).ReadProblemCodeAsync()).Should().Be("COLLECTED_AMOUNT_UNCHANGED");
        (await EditAsync(s.Token, s.CurrentInvoiceId, -1, 1_000_000, "x")).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await EditAsync(s.Token, Guid.NewGuid(), 1, 0)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        Collected(await SummaryAsync(s.Token, s.CurrentInvoiceId)).Should().Be(1_000_000);
    }

    [Fact]
    public async Task S7_Manager_CannotReduceMoneyCollectedBeforeLastMonth_OwnerCan()
    {
        var owner = await _client.CreateActiveOwnerAsync();
        var s = await ArrangeAsync(owner.Tokens.AccessToken);
        var old = s.Start.AddDays(-1); // trước ngày 1 tháng trước (thu trước khi vào ở)
        await PayAsync(s, 1_000_000, old, s.PreviousInvoiceId);
        await PayAsync(s, 1_000_000, s.Today, s.CurrentInvoiceId);
        var manager = await ManagerTokenAsync(s.Token, s.PropertyId);

        (await (await EditAsync(manager, s.PreviousInvoiceId, 500_000, 1_000_000, "Nhập nhầm")).ReadProblemCodeAsync()).Should().Be("COLLECTED_PERIOD_LOCKED");
        var oldReceipt = Active(await PaymentsAsync(s.Token, s.PreviousInvoiceId)).Single().GetProperty("id").GetGuid();
        (await (await _client.PostJsonAsync($"/api/v1/payments/{oldReceipt}/reverse", new { reason = "Nhập nhầm" }, manager)).ReadProblemCodeAsync())
            .Should().Be("COLLECTED_PERIOD_LOCKED");
        await OkAsync(await EditAsync(manager, s.PreviousInvoiceId, 2_000_000, 1_000_000)); // tăng: không giới hạn
        await OkAsync(await EditAsync(manager, s.CurrentInvoiceId, 800_000, 1_000_000, "Nhập nhầm")); // tiền thu tháng này: được

        var byOwner = await OkAsync(await EditAsync(s.Token, s.PreviousInvoiceId, 500_000, 2_000_000, "Chủ trọ sửa"));
        Collected(byOwner.GetProperty("summary")).Should().Be(500_000);
    }

    [Fact]
    public async Task S8_CannotReduceBelowRefundLinesPointingToTheInvoice()
    {
        var owner = await _client.CreateActiveOwnerAsync();
        var token = owner.Tokens.AccessToken;
        var today = TestData.Today(factory);
        var month = new DateOnly(today.Year, today.Month, 1);
        var start = month.AddMonths(-1);
        var propertyId = await _client.CreatePropertyAsync(token, anchorDay: 1, chargeMode: "Prepaid");
        var roomId = await _client.CreateRoomAsync(token, propertyId);
        var renter = await _client.CreateRenterAsync(token);
        var contractId = await (await _client.PostJsonAsync("/api/v1/contracts", new
        {
            roomId,
            contract = new { representativeRenterId = renter, startDate = start, monthlyRent = Rent, depositAmount = 0, occupants = new[] { new { renterId = renter } } }
        }, token)).ReadIdAsync();
        (await _client.PostJsonAsync($"/api/v1/contracts/{contractId}/activate", null, token)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        var source = await GenerateAndFinalizeAsync(token, propertyId, contractId, start);
        (await _client.PostJsonAsync($"/api/v1/contracts/{contractId}/payments", new { amount = Rent, method = "Cash", paidAt = today, invoiceId = source }, token))
            .StatusCode.Should().Be(HttpStatusCode.Created);
        (await _client.PostJsonAsync("/api/v1/invoices/generate", new { propertyId, billingMonth = $"{month:yyyy-MM}" }, token)).StatusCode.Should().Be(HttpStatusCode.OK);
        var page = await (await _client.GetAsync($"/api/v1/invoices?contractId={contractId}&billingMonth={month:yyyy-MM}", token)).ReadAsync<JsonElement>();
        var draft = page.GetProperty("items")[0].GetProperty("id").GetGuid();
        (await _client.AddInvoiceLineAsync(draft,
                new { type = "Refund", description = "Hoàn tiền thu nhầm", amount = 500_000, note = "Thu nhầm tháng trước", sourceInvoiceId = source }, token))
            .IsSuccessStatusCode.Should().BeTrue();

        (await (await EditAsync(token, source, 400_000, Rent, "Nhập nhầm")).ReadProblemCodeAsync()).Should().Be("COLLECTED_BELOW_REFUNDED");
        await OkAsync(await EditAsync(token, source, 500_000, Rent, "Nhập nhầm"));
    }

    /// <summary>Hóa đơn nguồn tháng trước đã thu đủ 3tr + hóa đơn tháng này (nháp) có dòng Hoàn trả 500k trỏ vào nguồn.</summary>
    private async Task<(string Token, Guid Source, Guid Draft, Guid ReceiptId)> ArrangeRefundAsync()
    {
        var owner = await _client.CreateActiveOwnerAsync();
        var token = owner.Tokens.AccessToken;
        var today = TestData.Today(factory);
        var month = new DateOnly(today.Year, today.Month, 1);
        var start = month.AddMonths(-1);
        var propertyId = await _client.CreatePropertyAsync(token, anchorDay: 1, chargeMode: "Prepaid");
        var roomId = await _client.CreateRoomAsync(token, propertyId);
        var renter = await _client.CreateRenterAsync(token);
        var contractId = await (await _client.PostJsonAsync("/api/v1/contracts", new
        {
            roomId,
            contract = new { representativeRenterId = renter, startDate = start, monthlyRent = Rent, depositAmount = 0, occupants = new[] { new { renterId = renter } } }
        }, token)).ReadIdAsync();
        (await _client.PostJsonAsync($"/api/v1/contracts/{contractId}/activate", null, token)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        var source = await GenerateAndFinalizeAsync(token, propertyId, contractId, start);
        (await _client.PostJsonAsync($"/api/v1/contracts/{contractId}/payments", new { amount = Rent, method = "Cash", paidAt = today, invoiceId = source }, token))
            .StatusCode.Should().Be(HttpStatusCode.Created);
        var receipt = (await PaymentsAsync(token, source)).Single().GetProperty("id").GetGuid();
        (await _client.PostJsonAsync("/api/v1/invoices/generate", new { propertyId, billingMonth = $"{month:yyyy-MM}" }, token)).StatusCode.Should().Be(HttpStatusCode.OK);
        var page = await (await _client.GetAsync($"/api/v1/invoices?contractId={contractId}&billingMonth={month:yyyy-MM}", token)).ReadAsync<JsonElement>();
        var draft = page.GetProperty("items")[0].GetProperty("id").GetGuid();
        (await _client.AddInvoiceLineAsync(draft,
                new { type = "Refund", description = "Hoàn tiền thu nhầm", amount = 500_000, note = "Thu nhầm tháng trước", sourceInvoiceId = source }, token))
            .IsSuccessStatusCode.Should().BeTrue();
        return (token, source, draft, receipt);
    }

    // M-01 / PM-BR-38 (6) áp cho nút Đảo: phiếu thu của phiếu nguồn không đảo được khi dòng Hoàn trả ĐÃ CHỐT trỏ vào nó (sổ ghi hoàn tiền chưa từng thu).
    [Fact]
    public async Task ReverseReceipt_RefusedWhileFinalizedRefundLinePointsToSource()
    {
        var (token, source, draft, receipt) = await ArrangeRefundAsync();
        var finalized = await _client.FinalizeInvoiceAsync(draft, token);
        finalized.StatusCode.Should().Be(HttpStatusCode.OK, await finalized.Content.ReadAsStringAsync());

        var refused = await _client.PostJsonAsync($"/api/v1/payments/{receipt}/reverse", new { reason = "Nhập nhầm phiếu" }, token);
        refused.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await refused.ReadProblemCodeAsync()).Should().Be("COLLECTED_BELOW_REFUNDED");
        Collected(await SummaryAsync(token, source)).Should().Be(Rent, "bị chặn ⇒ không đảo gì");

        // Hủy phiếu hoàn trước (void hóa đơn chứa dòng hoàn) thì đảo được.
        var voided = await _client.PostJsonAsync($"/api/v1/invoices/{draft}/void", new { reason = "Hoàn nhầm" }, token);
        voided.StatusCode.Should().Be(HttpStatusCode.OK, await voided.Content.ReadAsStringAsync());
        (await _client.PostJsonAsync($"/api/v1/payments/{receipt}/reverse", new { reason = "Nhập nhầm phiếu" }, token))
            .StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task ReverseReceipt_AllowedWhenRefundLineIsStillDraft()
    {
        var (token, source, _, receipt) = await ArrangeRefundAsync(); // dòng hoàn còn ở nháp — chốt sẽ kiểm lại BL-BR-27

        (await _client.PostJsonAsync($"/api/v1/payments/{receipt}/reverse", new { reason = "Nhập nhầm phiếu" }, token))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        Collected(await SummaryAsync(token, source)).Should().Be(0);
    }

    [Fact]
    public async Task DraftOrCarriedOver_Refused()
    {
        var owner = await _client.CreateActiveOwnerAsync();
        var token = owner.Tokens.AccessToken;
        var today = TestData.Today(factory);
        var month = new DateOnly(today.Year, today.Month, 1);
        var propertyId = await _client.CreatePropertyAsync(token, anchorDay: 1, chargeMode: "Prepaid");
        var roomId = await _client.CreateRoomAsync(token, propertyId);
        var renter = await _client.CreateRenterAsync(token);
        var contractId = await (await _client.PostJsonAsync("/api/v1/contracts", new
        {
            roomId,
            contract = new { representativeRenterId = renter, startDate = month, monthlyRent = Rent, depositAmount = 0, occupants = new[] { new { renterId = renter } } }
        }, token)).ReadIdAsync();
        (await _client.PostJsonAsync($"/api/v1/contracts/{contractId}/activate", null, token)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await _client.PostJsonAsync("/api/v1/invoices/generate", new { propertyId, billingMonth = $"{month:yyyy-MM}" }, token)).StatusCode.Should().Be(HttpStatusCode.OK);
        var page = await (await _client.GetAsync($"/api/v1/invoices?contractId={contractId}", token)).ReadAsync<JsonElement>();
        var draft = page.GetProperty("items")[0].GetProperty("id").GetGuid();

        (await (await EditAsync(token, draft, 1_000, 0)).ReadProblemCodeAsync()).Should().Be("INVOICE_NOT_FINALIZED");
    }

    private async Task<string> ManagerTokenAsync(string ownerToken, Guid propertyId)
    {
        var created = await _client.PostJsonAsync("/api/v1/org/members", new { fullName = "Quản Lý Khu", phone = TestApi.NewPhone(), propertyIds = new[] { propertyId } },
            ownerToken);
        created.StatusCode.Should().Be(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        var credentials = await created.ReadAsync<TemporaryCredentialsResponse>();
        var login = await _client.LoginAsync(credentials.Username, credentials.TemporaryPassword);
        return (await (await _client.PostJsonAsync("/api/v1/auth/change-password",
            new { currentPassword = credentials.TemporaryPassword, newPassword = "QuanLyKhu2026" }, login.AccessToken)).ReadAsync<TokenResponse>()).AccessToken;
    }
}
