using System.Net;
using System.Text.Json;
using renting_room.IntegrationTests.Infrastructure;

namespace renting_room.IntegrationTests;

/// <summary>
/// 28/10/2026 — M-10 (M05 CT-BR-02, M06 MT-BR-03 / MT-BR-13): mỗi HĐ có dải số riêng — người mới nhận phòng nhỏ hơn số của người cũ chỉ bị cảnh báo
/// (<c>HANDOVER_BELOW_PREVIOUS_FINAL</c>), chấp nhận thì người cũ giữ nguyên. Khu thu sau, điện 3.500đ/kWh, công tơ lắp ở 100.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class HandoverBelowPreviousTests(ApiFactory factory)
{
    private const decimal Price = 3_500;
    private readonly HttpClient _client = factory.CreateClient();

    private sealed record S(string Token, Guid PropertyId, Guid RoomId, Guid ContractId, Guid MeterId, DateOnly Start, DateOnly Today);

    /// <summary>HĐ A từ ngày 1 của 2 tháng trước, công tơ điện nhận phòng ở 100.</summary>
    private async Task<S> ArrangeAsync()
    {
        var token = (await _client.CreateActiveOwnerAsync()).Tokens.AccessToken;
        var today = TestData.Today(factory);
        var start = new DateOnly(today.Year, today.Month, 1).AddMonths(-2);
        var propertyId = await _client.CreatePropertyAsync(token, anchorDay: 1, chargeMode: "Postpaid");
        var roomId = await _client.CreateRoomAsync(token, propertyId);
        var electricity = await _client.FeeIdAsync(token, propertyId, "Điện");
        await _client.PostJsonAsync($"/api/v1/fee-types/{electricity}/prices", new { effectiveFrom = start.AddDays(-30), unitPrice = Price }, token);
        var meterId = await _client.InstallMeterAsync(token, roomId, electricity, start.AddDays(-1), 100);
        var contractId = await _client.CreateContractAsync(token, roomId, await _client.CreateRenterAsync(token), start);
        (await _client.PostJsonAsync($"/api/v1/contracts/{contractId}/activate",
            new { handoverReadings = new[] { new { meterId, value = (decimal?)null } } }, token)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        return new S(token, propertyId, roomId, contractId, meterId, start, today);
    }

    private Task<HttpResponseMessage> SavePeriodicAsync(S s, Guid contractId, DateOnly closing, decimal value, bool onAnchorDay = false) =>
        _client.PutJsonAsync($"/api/v1/properties/{s.PropertyId}/meter-readings", new
        {
            readings = new[]
            {
                new { meterId = s.MeterId, contractId, closingPeriodStart = closing,
                    readingDate = onAnchorDay ? closing.AddMonths(1) : closing.AddMonths(1).AddDays(-1), value }
            }
        }, s.Token);

    private async Task<JsonElement> GenerateAndFinalizeAsync(S s, Guid contractId, DateOnly month)
    {
        (await _client.PostJsonAsync("/api/v1/invoices/generate", new { propertyId = s.PropertyId, billingMonth = $"{month:yyyy-MM}" }, s.Token))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        var page = await (await _client.GetAsync($"/api/v1/invoices?contractId={contractId}&billingMonth={month:yyyy-MM}", s.Token)).ReadAsync<JsonElement>();
        var id = page.GetProperty("items")[0].GetProperty("id").GetGuid();
        var finalized = await _client.PostJsonAsync($"/api/v1/invoices/{id}/finalize", null, s.Token);
        finalized.StatusCode.Should().Be(HttpStatusCode.OK, await finalized.Content.ReadAsStringAsync());
        return await finalized.ReadAsync<JsonElement>();
    }

    private async Task<List<JsonElement>> ReadingsAsync(S s) =>
        await (await _client.GetAsync($"/api/v1/meters/{s.MeterId}/readings", s.Token)).ReadAsync<List<JsonElement>>();

    private static object Handover(Guid meterId, decimal value, bool accept) =>
        new { handoverReadings = new[] { new { meterId, value = (decimal?)value, acceptBelowPrevious = accept } } };

    /// <summary>A chốt kỳ nhầm 250 (thật 150), số cuối cũng 250 ngày <paramref name="end"/>, hóa đơn trả phòng đã thu, hoàn tất.</summary>
    private async Task EndWithHighReadingsAsync(S s, DateOnly end)
    {
        (await SavePeriodicAsync(s, s.ContractId, s.Start, 250)).StatusCode.Should().Be(HttpStatusCode.OK);
        await GenerateAndFinalizeAsync(s, s.ContractId, s.Start);
        (await _client.PostJsonAsync($"/api/v1/contracts/{s.ContractId}/liquidation/start", new
        {
            actualEndDate = end, reason = "MutualAgreement", finalReadings = new[] { new { meterId = s.MeterId, value = (decimal?)250 } }
        }, s.Token)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        var final = await _client.PostJsonAsync($"/api/v1/contracts/{s.ContractId}/final-invoice", new { finalReadings = (object?)null }, s.Token);
        final.StatusCode.Should().Be(HttpStatusCode.Created, await final.Content.ReadAsStringAsync());
        var finalId = (await final.ReadAsync<JsonElement>()).GetProperty("summary").GetProperty("id").GetGuid();
        var finalized = await (await _client.PostJsonAsync($"/api/v1/invoices/{finalId}/finalize", null, s.Token)).ReadAsync<JsonElement>();
        var total = finalized.GetProperty("summary").GetProperty("totalAmount").GetDecimal();
        (await _client.PostJsonAsync($"/api/v1/contracts/{s.ContractId}/payments",
            new { amount = total, method = "Cash", paidAt = s.Today, invoiceId = finalId }, s.Token)).StatusCode.Should().Be(HttpStatusCode.Created);
        // Công nợ tháng đầu chưa thu (chuyển vào hóa đơn trả phòng) đã nằm trong tổng trên.
        var done = await _client.PostJsonAsync($"/api/v1/contracts/{s.ContractId}/liquidation/complete", null, s.Token);
        done.StatusCode.Should().Be(HttpStatusCode.NoContent, await done.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task NewTenant_BelowPreviousTenantsReadings_AsksThenAccepts_OldContractUntouched_FirstInvoiceStartsFromNewNumber()
    {
        var s = await ArrangeAsync();
        var end = s.Start.AddMonths(1).AddDays(9);
        await EndWithHighReadingsAsync(s, end);
        var next = await _client.CreateContractAsync(s.Token, s.RoomId, await _client.CreateRenterAsync(s.Token), end.AddDays(1));

        var asked = await _client.PostJsonAsync($"/api/v1/contracts/{next}/activate", Handover(s.MeterId, 200, false), s.Token);
        asked.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        var problem = await asked.ReadAsync<JsonElement>();
        problem.GetProperty("code").GetString().Should().Be("HANDOVER_BELOW_PREVIOUS_FINAL");
        problem.GetProperty("previousValue").GetDecimal().Should().Be(250);

        var accepted = await _client.PostJsonAsync($"/api/v1/contracts/{next}/activate", Handover(s.MeterId, 200, true), s.Token);
        accepted.StatusCode.Should().Be(HttpStatusCode.NoContent, await accepted.Content.ReadAsStringAsync());

        var readings = await ReadingsAsync(s);
        readings.Single(r => r.GetProperty("kind").GetString() == "Final").GetProperty("value").GetDecimal().Should().Be(250, "người cũ giữ nguyên");
        readings.Single(r => r.GetProperty("kind").GetString() == "Periodic").GetProperty("value").GetDecimal().Should().Be(250);
        readings.Single(r => r.GetProperty("kind").GetString() == "Handover" && r.GetProperty("contractId").GetGuid() == next)
            .GetProperty("value").GetDecimal().Should().Be(200);
        (await (await _client.GetAsync($"/api/v1/rooms/{s.RoomId}/charges", s.Token)).ReadAsync<List<JsonElement>>())
            .Should().BeEmpty("không sinh khoản chênh lệch sau thanh lý");

        // Hóa đơn đầu của người mới tính từ 200 (không từ 250): cuối kỳ 230 ⇒ 30 kWh.
        var closing = end.AddDays(1);
        (await SavePeriodicAsync(s, next, closing, 230)).StatusCode.Should().Be(HttpStatusCode.OK);
        var invoice = await GenerateAndFinalizeAsync(s, next, new DateOnly(closing.Year, closing.Month, 1));
        invoice.GetProperty("lines").EnumerateArray().Single(l => l.GetProperty("type").GetString() == "Metered")
            .GetProperty("amount").GetDecimal().Should().Be(30 * Price);
    }

    [Fact]
    public async Task NewTenant_AtOrAbovePreviousTenantsReading_SameDayAsPreviousReading_IsAcceptedWithoutConfirmation()
    {
        var s = await ArrangeAsync();
        var end = s.Start.AddMonths(2).AddDays(-1);
        // Sáng ngày 1 đọc số chốt kỳ cuối của A (ghi ngày 1), A đã đi từ cuối tháng trước.
        (await SavePeriodicAsync(s, s.ContractId, s.Start, 150)).StatusCode.Should().Be(HttpStatusCode.OK);
        await GenerateAndFinalizeAsync(s, s.ContractId, s.Start);
        (await SavePeriodicAsync(s, s.ContractId, s.Start.AddMonths(1), 200, onAnchorDay: true)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await _client.PostJsonAsync($"/api/v1/contracts/{s.ContractId}/liquidation/start", new
        {
            actualEndDate = end, reason = "MutualAgreement", finalReadings = new[] { new { meterId = s.MeterId, value = (decimal?)200 } }
        }, s.Token)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        var next = await _client.CreateContractAsync(s.Token, s.RoomId, await _client.CreateRenterAsync(s.Token), end.AddDays(1));

        var activated = await _client.PostJsonAsync($"/api/v1/contracts/{next}/activate", Handover(s.MeterId, 205, false), s.Token);
        activated.StatusCode.Should().Be(HttpStatusCode.NoContent, await activated.Content.ReadAsStringAsync());

        (await ReadingsAsync(s)).First().GetProperty("value").GetDecimal().Should().Be(205, "số mới nhất của công tơ là số nhận phòng người mới");
    }

    [Fact]
    public async Task NewTenant_BelowMeterInitialReading_IsRefusedEvenWhenAccepting()
    {
        var s = await ArrangeAsync();
        var end = s.Start.AddDays(9);
        (await _client.PostJsonAsync($"/api/v1/contracts/{s.ContractId}/liquidation/start", new
        {
            actualEndDate = end, reason = "MutualAgreement", finalReadings = new[] { new { meterId = s.MeterId, value = (decimal?)120 } }
        }, s.Token)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        var next = await _client.CreateContractAsync(s.Token, s.RoomId, await _client.CreateRenterAsync(s.Token), end.AddDays(1));

        var refused = await _client.PostJsonAsync($"/api/v1/contracts/{next}/activate", Handover(s.MeterId, 90, true), s.Token);

        (await refused.ReadProblemCodeAsync()).Should().Be("READING_NOT_MONOTONIC", "không thể nhỏ hơn số lúc lắp công tơ (100)");
    }

    [Fact]
    public async Task Resign_WithHandoverBelowOldContractsLastReading_Accepted_KeepsOldContractUntouched()
    {
        var s = await ArrangeAsync();
        (await SavePeriodicAsync(s, s.ContractId, s.Start, 250)).StatusCode.Should().Be(HttpStatusCode.OK);
        var renter = (await (await _client.GetAsync($"/api/v1/contracts/{s.ContractId}", s.Token)).ReadAsync<JsonElement>())
            .GetProperty("representativeRenterId").GetGuid();
        var resigned = await _client.PostJsonAsync($"/api/v1/contracts/{s.ContractId}/re-sign", new { handoverDate = s.Today, representativeRenterId = renter }, s.Token);
        resigned.StatusCode.Should().Be(HttpStatusCode.Created, await resigned.Content.ReadAsStringAsync());
        var draft = await resigned.ReadIdAsync();

        (await (await _client.PostJsonAsync($"/api/v1/contracts/{draft}/activate", Handover(s.MeterId, 200, false), s.Token)).ReadProblemCodeAsync())
            .Should().Be("HANDOVER_BELOW_PREVIOUS_FINAL");
        var signed = await _client.PostJsonAsync($"/api/v1/contracts/{draft}/activate", Handover(s.MeterId, 200, true), s.Token);
        signed.StatusCode.Should().Be(HttpStatusCode.NoContent, await signed.Content.ReadAsStringAsync());

        var readings = await ReadingsAsync(s);
        readings.Single(r => r.GetProperty("kind").GetString() == "Final" && r.GetProperty("contractId").GetGuid() == s.ContractId)
            .GetProperty("value").GetDecimal().Should().Be(250, "số cuối HĐ cũ không thấp hơn số chốt kỳ 250 của chính HĐ đó");
        readings.Should().Contain(r => r.GetProperty("kind").GetString() == "Handover" && r.GetProperty("contractId").GetGuid() == draft
            && r.GetProperty("value").GetDecimal() == 200);
    }

    [Fact]
    public async Task TransferIntoRoomWithPreviousTenantsHigherReading_AsksThenAccepts()
    {
        var token = (await _client.CreateActiveOwnerAsync()).Tokens.AccessToken;
        var today = TestData.Today(factory);
        var start = TestData.StartWithinOnePeriod(today, 12);
        var propertyId = await _client.CreatePropertyAsync(token, anchorDay: start.Day, chargeMode: "Postpaid");
        var electricity = await _client.FeeIdAsync(token, propertyId, "Điện");
        await _client.PostJsonAsync($"/api/v1/fee-types/{electricity}/prices", new { effectiveFrom = start.AddDays(-30), unitPrice = Price }, token);
        var roomA = await _client.CreateRoomAsync(token, propertyId, "A1");
        var roomB = await _client.CreateRoomAsync(token, propertyId, "A2");
        var meterA = await _client.InstallMeterAsync(token, roomA, electricity, start.AddDays(-1), 100);
        var meterB = await _client.InstallMeterAsync(token, roomB, electricity, start.AddDays(-1), 100);
        async Task<Guid> ActiveAsync(Guid room, Guid meter)
        {
            var id = await _client.CreateContractAsync(token, room, await _client.CreateRenterAsync(token), start);
            (await _client.PostJsonAsync($"/api/v1/contracts/{id}/activate", new { handoverReadings = new[] { new { meterId = meter, value = (decimal?)null } } }, token))
                .StatusCode.Should().Be(HttpStatusCode.NoContent);
            return id;
        }
        var previousTenant = await ActiveAsync(roomB, meterB);
        (await _client.PostJsonAsync($"/api/v1/contracts/{previousTenant}/liquidation/start", new
        {
            actualEndDate = start.AddDays(3), reason = "MutualAgreement", finalReadings = new[] { new { meterId = meterB, value = (decimal?)250 } }
        }, token)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        var mover = await ActiveAsync(roomA, meterA);

        object Body(bool accept) => new
        {
            toRoomId = roomB, oldRoomReadings = new[] { new { meterId = meterA, value = (decimal?)110 } },
            newRoomReadings = new[] { new { meterId = meterB, value = (decimal?)200, acceptBelowPrevious = accept } }
        };
        (await (await _client.PostJsonAsync($"/api/v1/contracts/{mover}/transfer-room", Body(false), token)).ReadProblemCodeAsync())
            .Should().Be("HANDOVER_BELOW_PREVIOUS_FINAL");
        var moved = await _client.PostJsonAsync($"/api/v1/contracts/{mover}/transfer-room", Body(true), token);
        moved.StatusCode.Should().Be(HttpStatusCode.OK, await moved.Content.ReadAsStringAsync());
    }
}
