using System.Net;
using System.Text.Json;
using renting_room.IntegrationTests.Infrastructure;

namespace renting_room.IntegrationTests;

/// <summary>
/// 12/10/2026 — M07 BL-UC-01/02 (tạo phiếu nhiều khu / theo 1 HĐ, số tổng hợp), M03 RT-UC-05, RT-BR-05 (ghi nhận đồng ý, ẩn hồ sơ),
/// lọc người thuê theo khu / đang ở.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class BillingAndRenterExtrasTests(ApiFactory factory)
{
    private readonly HttpClient _client = factory.CreateClient();

    private async Task<(Guid RoomId, Guid ContractId, Guid RenterId)> ActiveContractAsync(string token, Guid propertyId, DateOnly start)
    {
        var roomId = await _client.CreateRoomAsync(token, propertyId);
        var renterId = await _client.CreateRenterAsync(token);
        var contractId = await _client.CreateContractAsync(token, roomId, renterId, start);
        (await _client.PostJsonAsync($"/api/v1/contracts/{contractId}/activate", null, token)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        return (roomId, contractId, renterId);
    }

    [Fact]
    public async Task GenerateForManyProperties_OrOneContract_AndTotalsCoverTheWholeFilter()
    {
        var owner = await _client.CreateActiveOwnerAsync();
        var token = owner.Tokens.AccessToken;
        var today = TestData.Today(factory);
        var month = new DateOnly(today.Year, today.Month, 1).AddMonths(-1);
        var billingMonth = $"{month:yyyy-MM}";
        var first = await _client.CreatePropertyAsync(token, anchorDay: 1, chargeMode: "Postpaid");
        var second = await _client.CreatePropertyAsync(token, anchorDay: 1, chargeMode: "Postpaid");
        var a = await ActiveContractAsync(token, first, month);
        await ActiveContractAsync(token, second, month);

        // Một lệnh cho mọi khu đang dùng; khu không thuộc tổ chức báo lỗi riêng, không làm hỏng khu khác.
        var bulk = await (await _client.PostJsonAsync("/api/v1/invoices/generate-bulk", new { billingMonth }, token)).ReadAsync<List<JsonElement>>();
        bulk.Select(r => r.GetProperty("propertyId").GetGuid()).Should().BeEquivalentTo([first, second]);
        bulk.Should().OnlyContain(r => r.GetProperty("result").GetProperty("created").GetInt32() == 1);
        var unknown = await (await _client.PostJsonAsync("/api/v1/invoices/generate-bulk", new { billingMonth, propertyIds = new[] { Guid.NewGuid() } }, token))
            .ReadAsync<List<JsonElement>>();
        unknown.Single().GetProperty("errorCode").GetString().Should().Be("PROPERTY_NOT_FOUND");

        // Theo 1 HĐ cụ thể: khu 1 thêm 2 HĐ mới, chỉ lập cho HĐ được chọn.
        var b = await ActiveContractAsync(token, first, month);
        await ActiveContractAsync(token, first, month);
        var one = await (await _client.PostJsonAsync("/api/v1/invoices/generate", new { propertyId = first, billingMonth, contractId = b.ContractId }, token))
            .ReadAsync<JsonElement>();
        one.GetProperty("created").GetInt32().Should().Be(1);
        one.GetProperty("skipped").GetArrayLength().Should().Be(0);

        // Chốt 1 phiếu, thu 1 triệu ⇒ số tổng hợp tính trên toàn bộ kết quả lọc.
        var page = await (await _client.GetAsync($"/api/v1/invoices?contractId={a.ContractId}", token)).ReadAsync<JsonElement>();
        var invoiceId = page.GetProperty("items")[0].GetProperty("id").GetGuid();
        (await _client.PostJsonAsync($"/api/v1/invoices/{invoiceId}/finalize", null, token)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await _client.PostJsonAsync($"/api/v1/contracts/{a.ContractId}/payments", new { amount = 1_000_000, method = "Cash", paidAt = today, invoiceId }, token))
            .StatusCode.Should().Be(HttpStatusCode.Created);

        // PM-UC-09: số nợ của hợp đồng vừa thu một phần; hợp đồng chỉ có nháp thì chưa nợ.
        var balance = await (await _client.GetAsync($"/api/v1/contracts/{a.ContractId}/balance", token)).ReadAsync<JsonElement>();
        balance.GetProperty("outstanding").GetDecimal().Should().Be(2_500_000);
        balance.GetProperty("overdueAmount").GetDecimal().Should().Be(0);
        balance.GetProperty("refundDue").GetDecimal().Should().Be(0);
        var owed = balance.GetProperty("unpaidInvoices").EnumerateArray().Single();
        owed.GetProperty("invoiceId").GetGuid().Should().Be(invoiceId);
        owed.GetProperty("billingMonth").GetString().Should().Be(billingMonth);
        owed.GetProperty("totalAmount").GetDecimal().Should().Be(3_500_000);
        owed.GetProperty("paidAmount").GetDecimal().Should().Be(1_000_000);
        owed.GetProperty("isOverdue").GetBoolean().Should().BeFalse();
        var draftOnly = await (await _client.GetAsync($"/api/v1/contracts/{b.ContractId}/balance", token)).ReadAsync<JsonElement>();
        draftOnly.GetProperty("outstanding").GetDecimal().Should().Be(0);
        draftOnly.GetProperty("draftInvoices").GetInt32().Should().Be(1);
        (await _client.GetAsync($"/api/v1/contracts/{Guid.NewGuid()}/balance", token)).StatusCode.Should().Be(HttpStatusCode.NotFound);

        var totals = await (await _client.GetAsync($"/api/v1/invoices/totals?billingMonth={billingMonth}", token)).ReadAsync<JsonElement>();
        totals.GetProperty("finalizedCount").GetInt32().Should().Be(1);
        totals.GetProperty("totalAmount").GetDecimal().Should().Be(3_500_000);
        totals.GetProperty("paidAmount").GetDecimal().Should().Be(1_000_000);
        totals.GetProperty("outstandingAmount").GetDecimal().Should().Be(2_500_000);
        totals.GetProperty("overdueAmount").GetDecimal().Should().Be(0, "chưa tới hạn thanh toán");
        totals.GetProperty("draftCount").GetInt32().Should().Be(2);
        totals.GetProperty("draftAmount").GetDecimal().Should().Be(7_000_000);
        var secondOnly = await (await _client.GetAsync($"/api/v1/invoices/totals?billingMonth={billingMonth}&propertyId={second}", token)).ReadAsync<JsonElement>();
        secondOnly.GetProperty("finalizedCount").GetInt32().Should().Be(0);
        secondOnly.GetProperty("draftCount").GetInt32().Should().Be(1);
    }

    [Fact]
    public async Task Renters_FilterByPropertyAndStaying_Archive_AndConsentRecords()
    {
        var owner = await _client.CreateActiveOwnerAsync();
        var token = owner.Tokens.AccessToken;
        var today = TestData.Today(factory);
        var first = await _client.CreatePropertyAsync(token);
        var second = await _client.CreatePropertyAsync(token);
        var staying = (await ActiveContractAsync(token, first, today)).RenterId;
        var other = (await ActiveContractAsync(token, second, today)).RenterId;
        var idle = await _client.CreateRenterAsync(token);

        async Task<List<Guid>> IdsAsync(string query) =>
            (await (await _client.GetAsync($"/api/v1/renters?pageSize=100{query}", token)).ReadAsync<JsonElement>()).GetProperty("items").EnumerateArray()
            .Select(r => r.GetProperty("id").GetGuid()).ToList();
        (await IdsAsync($"&propertyId={first}")).Should().Equal(staying);
        (await IdsAsync("&staying=true")).Should().BeEquivalentTo([staying, other]);
        (await IdsAsync("&staying=false")).Should().Equal(idle);

        // Ẩn hồ sơ: người đang có HĐ không ẩn được; người không còn thuê thì ẩn / hiện lại được.
        (await (await _client.PostJsonAsync($"/api/v1/renters/{staying}/archive", null, token)).ReadProblemCodeAsync()).Should().Be("RENTER_HAS_ACTIVE_CONTRACT");
        (await _client.PostJsonAsync($"/api/v1/renters/{idle}/archive", null, token)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await IdsAsync("")).Should().NotContain(idle);
        (await IdsAsync("&includeArchived=true")).Should().Contain(idle);
        (await _client.PostJsonAsync($"/api/v1/renters/{idle}/restore", null, token)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await IdsAsync("")).Should().Contain(idle);

        // Ghi nhận đồng ý xử lý dữ liệu, rồi rút lại.
        var consents = $"/api/v1/renters/{idle}/consents";
        var added = await _client.PostJsonAsync(consents, new { purpose = "contract_management", method = "ContractClause", givenAt = today, note = "Điều 9 HĐ" }, token);
        added.StatusCode.Should().Be(HttpStatusCode.OK, await added.Content.ReadAsStringAsync());
        var consentId = (await added.ReadAsync<JsonElement>()).GetProperty("id").GetGuid();
        (await (await _client.PostJsonAsync(consents, new { purpose = "x", method = "Verbal", givenAt = today.AddDays(1) }, token)).ReadProblemCodeAsync())
            .Should().NotBeNull("ngày đồng ý không ở tương lai");
        (await _client.PostJsonAsync($"{consents}/{consentId}/withdraw", new { withdrawnOn = today }, token)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await (await _client.PostJsonAsync($"{consents}/{consentId}/withdraw", new { withdrawnOn = today }, token)).ReadProblemCodeAsync())
            .Should().Be("CONSENT_ALREADY_WITHDRAWN");
        var list = await (await _client.GetAsync(consents, token)).ReadAsync<List<JsonElement>>();
        list.Should().ContainSingle().Which.GetProperty("withdrawnAt").GetString().Should().Be($"{today:yyyy-MM-dd}");
    }
}
