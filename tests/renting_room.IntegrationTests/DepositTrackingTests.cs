using System.Net;
using System.Text.Json;
using renting_room.IntegrationTests.Infrastructure;

namespace renting_room.IntegrationTests;

/// <summary>M08 PM-BR-32..35: cọc chỉ theo dõi — có cọc / bao nhiêu / đã hoàn trả chưa; không chặn hủy, thanh lý.</summary>
[Collection(ApiCollection.Name)]
public sealed class DepositTrackingTests(ApiFactory factory)
{
    private const decimal Deposit = 3_500_000; // cọc mặc định của TestData
    private readonly HttpClient _client = factory.CreateClient();

    private async Task<JsonElement> DepositOfAsync(string token, Guid contractId) =>
        (await (await _client.GetAsync($"/api/v1/contracts/{contractId}", token)).ReadAsync<JsonElement>()).GetProperty("deposit");

    private Task<HttpResponseMessage> RefundAsync(string token, Guid contractId, DateOnly on, decimal? amount, string? note) =>
        _client.PostJsonAsync($"/api/v1/contracts/{contractId}/deposit/refund", new { refundedOn = on, amount, note }, token);

    [Fact]
    public async Task Refund_FullOrPartialWithNote_Undo_RejectsOverAmountFutureDateAndTwice()
    {
        var owner = await _client.CreateActiveOwnerAsync();
        var token = owner.Tokens.AccessToken;
        var today = TestData.Today(factory);
        var (_, _, _, contractId) = await _client.CreateActiveContractAsync(token, today);

        var deposit = await DepositOfAsync(token, contractId);
        deposit.GetProperty("amount").GetDecimal().Should().Be(Deposit);
        deposit.GetProperty("status").GetString().Should().Be("Holding", "có cọc ⇒ mặc định đang giữ");

        (await (await RefundAsync(token, contractId, today, 1_000_000, null)).ReadProblemCodeAsync())
            .Should().Be("INVALID_REFUND_AMOUNT", "trả ít hơn cọc phải ghi lý do");
        (await (await RefundAsync(token, contractId, today, Deposit + 1, "Thừa")).ReadProblemCodeAsync()).Should().Be("INVALID_REFUND_AMOUNT");
        (await (await RefundAsync(token, contractId, today.AddDays(1), null, null)).ReadProblemCodeAsync()).Should().Be("INVALID_REFUND_DATE");

        (await RefundAsync(token, contractId, today, 3_000_000, "Giữ lại 500k sửa tường")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        deposit = await DepositOfAsync(token, contractId);
        deposit.GetProperty("status").GetString().Should().Be("Refunded");
        deposit.GetProperty("refundedAmount").GetDecimal().Should().Be(3_000_000);
        deposit.GetProperty("refundedOn").GetString().Should().Be($"{today:yyyy-MM-dd}");
        (await (await RefundAsync(token, contractId, today, null, null)).ReadProblemCodeAsync()).Should().Be("DEPOSIT_ALREADY_REFUNDED");

        (await _client.DeleteAsync($"/api/v1/contracts/{contractId}/deposit/refund", token)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await DepositOfAsync(token, contractId)).GetProperty("status").GetString().Should().Be("Holding", "bỏ đánh dấu khi nhập nhầm");
        (await (await _client.DeleteAsync($"/api/v1/contracts/{contractId}/deposit/refund", token)).ReadProblemCodeAsync())
            .Should().Be("DEPOSIT_NOT_REFUNDED");
    }

    [Fact]
    public async Task CompleteLiquidation_NotBlockedByDeposit_ShowsReminderUntilRefunded_NoDepositContractRejected()
    {
        var owner = await _client.CreateActiveOwnerAsync();
        var token = owner.Tokens.AccessToken;
        var today = TestData.Today(factory);
        var start = TestData.StartWithinOnePeriod(today, 10);
        var (propertyId, _, _, contractId) = await _client.CreateActiveContractAsync(token, start, anchorDay: start.Day);
        await _client.PostJsonAsync($"/api/v1/contracts/{contractId}/liquidation/start", new { actualEndDate = today, reason = "MutualAgreement" }, token);
        await _client.SettleAndCompleteAsync(token, contractId);

        (await DepositOfAsync(token, contractId)).GetProperty("notRefundedReminder").GetBoolean().Should().BeTrue("đã kết thúc mà chưa hoàn cọc");
        var list = await (await _client.GetAsync($"/api/v1/contracts?propertyId={propertyId}&depositNotRefunded=true", token)).ReadAsync<JsonElement>();
        list.GetProperty("items").EnumerateArray().Select(c => c.GetProperty("id").GetGuid()).Should().Contain(contractId);

        (await RefundAsync(token, contractId, today, null, null)).StatusCode.Should().Be(HttpStatusCode.NoContent, "HĐ đã kết thúc vẫn ghi hoàn cọc được");
        (await DepositOfAsync(token, contractId)).GetProperty("notRefundedReminder").GetBoolean().Should().BeFalse();

        var room = await _client.CreateRoomAsync(token, propertyId);
        var noDeposit = await (await _client.PostJsonAsync("/api/v1/contracts", new
        {
            roomId = room, contract = new { representativeRenterId = await _client.CreateRenterAsync(token), startDate = today, monthlyRent = 3_000_000, depositAmount = 0 }
        }, token)).ReadIdAsync();
        (await DepositOfAsync(token, noDeposit)).GetProperty("status").ValueKind.Should().Be(JsonValueKind.Null, "HĐ không cọc");
        (await (await RefundAsync(token, noDeposit, today, null, null)).ReadProblemCodeAsync()).Should().Be("NO_DEPOSIT");
    }
}
