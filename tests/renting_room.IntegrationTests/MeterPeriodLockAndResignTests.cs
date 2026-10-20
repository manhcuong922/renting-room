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

    [Fact]
    public async Task Resign_RoommateWithPlatedVehicle_EndsOldRegistration_CopiesToNewDraft()
    {
        var t = await TenancyAsync();
        var plate = $"29B1-{Random.Shared.Next(10_000, 99_999)}";
        (await _client.PostJsonAsync($"/api/v1/contracts/{t.OldContract}/vehicles",
            new { renterId = t.Roommate, vehicleType = "Motorbike", plateNumber = plate, registeredFrom = t.Today.AddDays(-30) }, t.Token))
            .StatusCode.Should().Be(HttpStatusCode.Created);

        var resigned = await ResignAsync(t);
        resigned.StatusCode.Should().Be(HttpStatusCode.Created, await resigned.Content.ReadAsStringAsync());
        var newId = await resigned.ReadIdAsync();

        var oldVehicle = (await DetailAsync(t.OldContract, t.Token)).GetProperty("vehicles").EnumerateArray().Single();
        oldVehicle.GetProperty("registeredTo").GetString().Should().Be($"{t.Today:yyyy-MM-dd}", "xe kết thúc ở HĐ cũ tại ngày bàn giao");
        var newVehicle = (await DetailAsync(newId, t.Token)).GetProperty("vehicles").EnumerateArray().Single();
        newVehicle.GetProperty("registeredTo").ValueKind.Should().Be(JsonValueKind.Null);
        newVehicle.GetProperty("renterId").GetGuid().Should().Be(t.Roommate);
    }

    [Fact]
    public async Task ResignNotCompleted_CancelDraftOrCancelLiquidation_DepositBackToHolding()
    {
        async Task<string?> DepositStatusAsync(Guid contractId, string token) =>
            (await DetailAsync(contractId, token)).GetProperty("deposit").GetProperty("status").GetString();

        // (1) Hủy HĐ nháp ký lại ⇒ cọc HĐ cũ về "Đang giữ".
        var first = await TenancyAsync();
        var draft = await (await ResignAsync(first)).ReadIdAsync();
        (await DepositStatusAsync(first.OldContract, first.Token)).Should().Be("Transferred");
        (await _client.PostJsonAsync($"/api/v1/contracts/{draft}/cancel", new { reason = "Người thuê đổi ý" }, first.Token)).IsSuccessStatusCode.Should().BeTrue();
        (await DepositStatusAsync(first.OldContract, first.Token)).Should().Be("Holding");

        // (2) Hủy thanh lý HĐ cũ ⇒ cọc về "Đang giữ".
        var second = await TenancyAsync(first.Token);
        (await ResignAsync(second)).StatusCode.Should().Be(HttpStatusCode.Created);
        (await DepositStatusAsync(second.OldContract, second.Token)).Should().Be("Transferred");
        (await _client.PostJsonAsync($"/api/v1/contracts/{second.OldContract}/liquidation/cancel", null, second.Token))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        (await DepositStatusAsync(second.OldContract, second.Token)).Should().Be("Holding");
    }

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

    private static IEnumerable<string?> WarningCodes(JsonElement body) =>
        body.GetProperty("warnings").EnumerateArray().Select(w => w.GetProperty("code").GetString());

    [Fact]
    public async Task ResignNotCompleted_CancelDraft_ReopensVehiclesEndedByResign_LeavesEarlierEndedOnes()
    {
        var t = await TenancyAsync();
        var (moving, gone) = (Plate(), Plate());
        await RegisterVehicleAsync(t.OldContract, t.Roommate, moving, t.Today.AddDays(-30), t.Token);
        var goneId = await RegisterVehicleAsync(t.OldContract, t.Roommate, gone, t.Today.AddDays(-30), t.Token);
        (await _client.PostJsonAsync($"/api/v1/contracts/{t.OldContract}/vehicles/{goneId}/end", new { endDate = t.Today.AddDays(-5) }, t.Token))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);
        var draft = await (await ResignAsync(t)).ReadIdAsync();

        var cancelled = await _client.PostJsonAsync($"/api/v1/contracts/{draft}/cancel", new { reason = "Người thuê đổi ý" }, t.Token);

        cancelled.StatusCode.Should().Be(HttpStatusCode.OK, await cancelled.Content.ReadAsStringAsync());
        WarningCodes(await cancelled.ReadAsync<JsonElement>()).Should().BeEmpty();
        (await VehicleAsync(t.OldContract, moving, t.Token)).GetProperty("registeredTo").ValueKind
            .Should().Be(JsonValueKind.Null, "xe kết thúc lúc ký lại được mở lại trên HĐ cũ");
        (await VehicleAsync(t.OldContract, gone, t.Token)).GetProperty("registeredTo").GetString()
            .Should().Be($"{t.Today.AddDays(-5):yyyy-MM-dd}", "xe kết thúc trước lần ký lại không bị đụng");
        (await VehicleAsync(draft, moving, t.Token)).GetProperty("registeredTo").ValueKind.Should().NotBe(JsonValueKind.Null);
    }

    [Fact]
    public async Task ResignNotCompleted_CancelLiquidation_EndsSamePlateOnDraft_ReopensOldVehicle()
    {
        var t = await TenancyAsync();
        var plate = Plate();
        await RegisterVehicleAsync(t.OldContract, t.Roommate, plate, t.Today.AddDays(-30), t.Token);
        var draft = await (await ResignAsync(t)).ReadIdAsync();

        var cancelled = await _client.PostJsonAsync($"/api/v1/contracts/{t.OldContract}/liquidation/cancel", null, t.Token);

        cancelled.StatusCode.Should().Be(HttpStatusCode.OK, await cancelled.Content.ReadAsStringAsync());
        WarningCodes(await cancelled.ReadAsync<JsonElement>()).Should().BeEmpty();
        (await VehicleAsync(t.OldContract, plate, t.Token)).GetProperty("registeredTo").ValueKind.Should().Be(JsonValueKind.Null);
        (await VehicleAsync(draft, plate, t.Token)).GetProperty("registeredTo").ValueKind
            .Should().NotBe(JsonValueKind.Null, "nháp ký lại nhả biển số trước khi HĐ cũ mở lại xe");
    }

    [Fact]
    public async Task ResignNotCompleted_PlateRegisteredElsewhere_WarnsVehicleNotRestored()
    {
        var t = await TenancyAsync();
        var plate = Plate();
        await RegisterVehicleAsync(t.OldContract, t.Roommate, plate, t.Today.AddDays(-30), t.Token);
        var draft = await (await ResignAsync(t)).ReadIdAsync();
        var onDraft = (await VehicleAsync(draft, plate, t.Token)).GetProperty("id").GetGuid();
        (await _client.PostJsonAsync($"/api/v1/contracts/{draft}/vehicles/{onDraft}/end", new { endDate = t.Today.AddDays(1) }, t.Token))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);
        var other = await TenancyAsync(t.Token);
        await RegisterVehicleAsync(other.OldContract, null, plate, t.Today, t.Token);

        var cancelled = await _client.PostJsonAsync($"/api/v1/contracts/{draft}/cancel", new { reason = "Người thuê đổi ý" }, t.Token);

        cancelled.StatusCode.Should().Be(HttpStatusCode.OK, await cancelled.Content.ReadAsStringAsync());
        var body = await cancelled.ReadAsync<JsonElement>();
        WarningCodes(body).Should().Equal("VEHICLE_NOT_RESTORED");
        body.GetProperty("warnings")[0].GetProperty("message").GetString().Should().Contain(plate.Replace("-", ""));
        (await VehicleAsync(t.OldContract, plate, t.Token)).GetProperty("registeredTo").GetString()
            .Should().Be($"{t.Today:yyyy-MM-dd}", "biển số đang ở HĐ khác ⇒ xe giữ trạng thái kết thúc");
    }
}
