using System.Net;
using System.Text.Json;
using renting_room.IntegrationTests.Infrastructure;

namespace renting_room.IntegrationTests;

/// <summary>
/// Bổ sung 17/10/2026: thay / tháo công tơ không lùi vào kỳ đã chốt phiếu (M06 MT-BR-09/10); ký lại kết thúc xe ở HĐ cũ (M05 CT-UC-21);
/// ký lại không thành ⇒ cọc HĐ cũ về "Đang giữ" (M08 PM-BR-34).
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class MeterPeriodLockAndResignTests(ApiFactory factory)
{
    private readonly HttpClient _client = factory.CreateClient();

    private async Task<JsonElement> DetailAsync(Guid contractId, string token) =>
        await (await _client.GetAsync($"/api/v1/contracts/{contractId}", token)).ReadAsync<JsonElement>();

    [Fact]
    public async Task ReplaceMeter_DateInsideFinalizedPeriod_IsRefused_AfterItTheOldMeterTailIsBilledNextPeriod()
    {
        var owner = await _client.CreateActiveOwnerAsync();
        var token = owner.Tokens.AccessToken;
        var today = TestData.Today(factory);
        var month = new DateOnly(today.Year, today.Month, 1);
        var previous = month.AddMonths(-1);
        var propertyId = await _client.CreatePropertyAsync(token, anchorDay: 1, chargeMode: "Postpaid");
        var electricity = await _client.FeeIdAsync(token, propertyId, "Điện");
        await _client.PostJsonAsync($"/api/v1/fee-types/{electricity}/prices", new { effectiveFrom = previous.AddDays(-30), unitPrice = 3500 }, token);
        var roomId = await _client.CreateRoomAsync(token, propertyId);
        var meterId = await _client.InstallMeterAsync(token, roomId, electricity, previous.AddDays(-1), 100);
        var contractId = await _client.CreateContractAsync(token, roomId, await _client.CreateRenterAsync(token), previous);
        (await _client.PostJsonAsync($"/api/v1/contracts/{contractId}/activate",
            new { handoverReadings = new[] { new { meterId, value = (decimal?)null } } }, token)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        Task<HttpResponseMessage> SaveAsync(Guid meter, DateOnly closing, DateOnly on, decimal value) => _client.PutJsonAsync(
            $"/api/v1/properties/{propertyId}/meter-readings",
            new { readings = new[] { new { meterId = meter, contractId, closingPeriodStart = closing, readingDate = on, value } } }, token);
        async Task<JsonElement> InvoiceAsync(DateOnly billingMonth)
        {
            (await (await _client.PostJsonAsync("/api/v1/invoices/generate", new { propertyId, billingMonth = $"{billingMonth:yyyy-MM}" }, token))
                .ReadAsync<JsonElement>()).GetProperty("created").GetInt32().Should().Be(1);
            var page = await (await _client.GetAsync($"/api/v1/invoices?contractId={contractId}&billingMonth={billingMonth:yyyy-MM}", token)).ReadAsync<JsonElement>();
            return await (await _client.GetAsync($"/api/v1/invoices/{page.GetProperty("items")[0].GetProperty("id").GetGuid()}", token)).ReadAsync<JsonElement>();
        }

        // Kỳ tháng trước đã chốt phiếu tới chỉ số 150 (ghi ngày 1 tháng này).
        (await SaveAsync(meterId, previous, month, 150)).StatusCode.Should().Be(HttpStatusCode.OK);
        var closed = await InvoiceAsync(previous);
        (await _client.PostJsonAsync($"/api/v1/invoices/{closed.GetProperty("summary").GetProperty("id").GetGuid()}/finalize", null, token))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        // Khai thay công tơ lùi vào kỳ đã chốt ⇒ chặn (cả thay từng phòng lẫn thay hàng loạt).
        var lockedDay = month.AddDays(-2);
        var refused = await _client.PostJsonAsync($"/api/v1/meters/{meterId}/replace",
            new { date = lockedDay, oldFinalValue = 160, newSerialNo = "E-NEW", newInitialValue = 0 }, token);
        refused.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await refused.ReadProblemCodeAsync()).Should().Be("METER_PERIOD_LOCKED");
        var bulk = await _client.PostJsonAsync($"/api/v1/properties/{propertyId}/meters/bulk-replace",
            new { feeTypeId = electricity, replacedOn = lockedDay, rows = new[] { new { meterId, oldFinalValue = 160m } } }, token);
        (await bulk.Content.ReadAsStringAsync()).Should().Contain("METER_PERIOD_LOCKED");

        // Ngày đầu kỳ chưa chốt ⇒ được; phiếu tháng này cộng phần còn lại của công tơ cũ (150 → 160) với công tơ mới (0 → 20).
        var replaced = await _client.PostJsonAsync($"/api/v1/meters/{meterId}/replace",
            new { date = month, oldFinalValue = 160, newSerialNo = "E-NEW", newInitialValue = 0 }, token);
        replaced.StatusCode.Should().Be(HttpStatusCode.Created, await replaced.Content.ReadAsStringAsync());
        var newMeter = await replaced.ReadIdAsync();
        (await SaveAsync(newMeter, month, today, 20)).StatusCode.Should().Be(HttpStatusCode.OK);
        var current = await InvoiceAsync(month);
        current.GetProperty("lines").EnumerateArray().Single(l => l.GetProperty("type").GetString() == "Metered").GetProperty("quantity").GetDecimal()
            .Should().Be(10 + 20, "sản lượng công tơ cũ sau chỉ số cuối kỳ không bị mất");
    }

    /// <summary>Phòng: người đứng tên + bạn ở cùng (có xe 29B1-12345), cọc 3,5tr; người đứng tên rời đi, bạn ở cùng ký lại từ hôm nay.</summary>
    private async Task<(string Token, Guid OldContract, Guid Roommate, DateOnly Today)> TenancyAsync(string? token = null)
    {
        token ??= (await _client.CreateActiveOwnerAsync()).Tokens.AccessToken;
        var today = TestData.Today(factory);
        var roomId = await _client.CreateRoomAsync(token, await _client.CreatePropertyAsync(token, anchorDay: 1));
        var representative = await _client.CreateRenterAsync(token);
        var roommate = await _client.CreateRenterAsync(token);
        var contractId = await _client.CreateContractAsync(token, roomId, representative, today.AddDays(-30), representative, roommate);
        (await _client.PostJsonAsync($"/api/v1/contracts/{contractId}/activate", null, token)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        return (token, contractId, roommate, today);
    }

    private Task<HttpResponseMessage> ResignAsync((string Token, Guid OldContract, Guid Roommate, DateOnly Today) t) =>
        _client.PostJsonAsync($"/api/v1/contracts/{t.OldContract}/re-sign", new { handoverDate = t.Today, representativeRenterId = t.Roommate }, t.Token);

    private async Task<string?> DepositStatusAsync(Guid contractId, string token) =>
        (await DetailAsync(contractId, token)).GetProperty("deposit").GetProperty("status").GetString();

    private static string Plate() => $"29B1-{Random.Shared.Next(10_000, 99_999)}";

    private async Task<Guid> RegisterVehicleAsync(Guid contractId, Guid? renterId, string plate, DateOnly from, string token)
    {
        var registered = await _client.PostJsonAsync($"/api/v1/contracts/{contractId}/vehicles",
            new { renterId, vehicleType = "Motorbike", plateNumber = plate, registeredFrom = from }, token);
        registered.StatusCode.Should().Be(HttpStatusCode.Created, await registered.Content.ReadAsStringAsync());
        return await registered.ReadIdAsync();
    }

    private async Task<JsonElement> VehicleAsync(Guid contractId, string plate, string token) =>
        (await DetailAsync(contractId, token)).GetProperty("vehicles").EnumerateArray()
        .Single(v => v.GetProperty("plateNumber").GetString() == plate.Replace("-", ""));

    /// <summary>27/10/2026: nháp ký lại là bản soạn — HĐ cũ vẫn hiệu lực bình thường (hóa đơn, xe, cọc) cho tới khi ký.</summary>
    [Fact]
    public async Task Resign_Draft_ChangesNothingOnOldContract_AndOnlyOneDraftPerContract()
    {
        var t = await TenancyAsync();
        var plate = Plate();
        await RegisterVehicleAsync(t.OldContract, t.Roommate, plate, t.Today.AddDays(-30), t.Token);

        var resigned = await ResignAsync(t);
        resigned.StatusCode.Should().Be(HttpStatusCode.Created, await resigned.Content.ReadAsStringAsync());
        var draftId = await resigned.ReadIdAsync();

        var old = await DetailAsync(t.OldContract, t.Token);
        old.GetProperty("status").GetString().Should().Be("Active", "nháp ký lại chưa ký ⇒ HĐ cũ vẫn hiệu lực");
        old.GetProperty("actualEndDate").ValueKind.Should().Be(JsonValueKind.Null);
        old.GetProperty("deposit").GetProperty("status").GetString().Should().Be("Holding");
        (await VehicleAsync(t.OldContract, plate, t.Token)).GetProperty("registeredTo").ValueKind.Should().Be(JsonValueKind.Null);
        var draft = await DetailAsync(draftId, t.Token);
        draft.GetProperty("status").GetString().Should().Be("Draft");
        draft.GetProperty("previousContractId").GetGuid().Should().Be(t.OldContract);
        draft.GetProperty("vehicles").EnumerateArray().Should().BeEmpty("xe chỉ chuyển sang HĐ mới khi ký");

        var second = await ResignAsync(t);
        second.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await second.ReadProblemCodeAsync()).Should().Be("RESIGN_DRAFT_EXISTS");
    }

    [Fact]
    public async Task Resign_CancelDraft_LeavesOldContractUntouched_AndAllowsANewDraft()
    {
        var t = await TenancyAsync();
        var plate = Plate();
        await RegisterVehicleAsync(t.OldContract, t.Roommate, plate, t.Today.AddDays(-30), t.Token);
        var draft = await (await ResignAsync(t)).ReadIdAsync();

        var cancelled = await _client.PostJsonAsync($"/api/v1/contracts/{draft}/cancel", new { reason = "Người thuê đổi ý" }, t.Token);

        cancelled.StatusCode.Should().Be(HttpStatusCode.OK, await cancelled.Content.ReadAsStringAsync());
        var old = await DetailAsync(t.OldContract, t.Token);
        old.GetProperty("status").GetString().Should().Be("Active");
        old.GetProperty("deposit").GetProperty("status").GetString().Should().Be("Holding");
        (await VehicleAsync(t.OldContract, plate, t.Token)).GetProperty("registeredTo").ValueKind.Should().Be(JsonValueKind.Null);
        (await ResignAsync(t)).StatusCode.Should().Be(HttpStatusCode.Created, "nháp cũ đã hủy ⇒ soạn lại được");
    }

    [Fact]
    public async Task Resign_Sign_LiquidatesOldAtHandover_MovesVehiclesAndDeposit()
    {
        var t = await TenancyAsync();
        var plate = Plate();
        await RegisterVehicleAsync(t.OldContract, t.Roommate, plate, t.Today.AddDays(-30), t.Token);
        var draft = await (await ResignAsync(t)).ReadIdAsync();

        var signed = await _client.PostJsonAsync($"/api/v1/contracts/{draft}/activate", null, t.Token);

        signed.StatusCode.Should().Be(HttpStatusCode.NoContent, await signed.Content.ReadAsStringAsync());
        var old = await DetailAsync(t.OldContract, t.Token);
        old.GetProperty("status").GetString().Should().Be("Liquidating");
        old.GetProperty("actualEndDate").GetString().Should().Be($"{t.Today:yyyy-MM-dd}", "ngày bàn giao");
        old.GetProperty("deposit").GetProperty("status").GetString().Should().Be("Transferred", "ký lại ⇒ cọc chuyển sang HĐ mới");
        (await VehicleAsync(t.OldContract, plate, t.Token)).GetProperty("registeredTo").GetString().Should().Be($"{t.Today:yyyy-MM-dd}");
        (await DetailAsync(draft, t.Token)).GetProperty("status").GetString().Should().Be("Active");
        var moved = await VehicleAsync(draft, plate, t.Token);
        moved.GetProperty("registeredTo").ValueKind.Should().Be(JsonValueKind.Null);
        moved.GetProperty("renterId").GetGuid().Should().Be(t.Roommate);
    }

    [Fact]
    public async Task Resign_WithoutDepositTransfer_OldDepositStaysHolding_AfterSign()
    {
        var t = await TenancyAsync();
        var resigned = await _client.PostJsonAsync($"/api/v1/contracts/{t.OldContract}/re-sign",
            new { handoverDate = t.Today, representativeRenterId = t.Roommate, transferDeposit = false }, t.Token);
        var draft = await resigned.ReadIdAsync();

        (await _client.PostJsonAsync($"/api/v1/contracts/{draft}/activate", null, t.Token)).StatusCode.Should().Be(HttpStatusCode.NoContent);

        (await DepositStatusAsync(t.OldContract, t.Token)).Should().Be("Holding", "chủ trọ chọn không chuyển cọc sang HĐ mới");
    }
}
