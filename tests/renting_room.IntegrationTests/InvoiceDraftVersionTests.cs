using System.Net;
using System.Text.Json;
using renting_room.IntegrationTests.Infrastructure;

namespace renting_room.IntegrationTests;

/// <summary>
/// 05/11/2026 — M07 §10 "Sửa / chốt nháp đồng thời 2 tab": mọi thao tác trên một phiếu nháp gửi <c>version</c> đang thấy; lệch ⇒ 409, phiếu không đổi.
/// VD: A mở phiếu; B thêm giảm trừ 300.000đ; A bấm chốt trên màn hình cũ ⇒ 409 thay vì chốt số A chưa thấy.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class InvoiceDraftVersionTests(ApiFactory factory)
{
    private readonly HttpClient _client = factory.CreateClient();

    private async Task<(string Token, Guid InvoiceId)> DraftAsync()
    {
        var token = (await _client.CreateActiveOwnerAsync()).Tokens.AccessToken;
        var start = TestData.StartWithinOnePeriod(TestData.Today(factory), 5);
        var (propertyId, roomId, _, contractId) = await _client.CreateActiveContractAsync(token, start, anchorDay: start.Day);
        var generated = await (await _client.PostJsonAsync("/api/v1/invoices/generate",
            new { propertyId, billingMonth = $"{start:yyyy-MM}", roomIds = new[] { roomId } }, token)).ReadAsync<JsonElement>();
        generated.GetProperty("created").GetInt32().Should().Be(1, generated.ToString());
        var page = await (await _client.GetAsync($"/api/v1/invoices?contractId={contractId}", token)).ReadAsync<JsonElement>();
        return (token, page.GetProperty("items")[0].GetProperty("id").GetGuid());
    }

    private async Task<JsonElement> DetailAsync(Guid id, string token) =>
        await (await _client.GetAsync($"/api/v1/invoices/{id}", token)).ReadAsync<JsonElement>();

    private static string Version(JsonElement detail) => detail.GetProperty("summary").GetProperty("version").GetString()!;

    private static decimal Total(JsonElement detail) => detail.GetProperty("summary").GetProperty("totalAmount").GetDecimal();

    private static Guid LineId(JsonElement detail, string type) =>
        detail.GetProperty("lines").EnumerateArray().First(l => l.GetProperty("type").GetString() == type).GetProperty("id").GetGuid();

    [Fact]
    public async Task StaleVersion_EveryDraftAction_Returns409_AndChangesNothing_ReloadThenFinalizeWorks()
    {
        var (token, id) = await DraftAsync();
        var seenByA = await DetailAsync(id, token);
        var staleVersion = Version(seenByA);
        var rentLine = LineId(seenByA, "Rent");

        // B thêm giảm trừ 300.000đ.
        (await _client.AddInvoiceLineAsync(id, new { type = "ManualDiscount", description = "Mất nước", amount = 300_000, note = "Sự cố bồn nước" }, token))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        var afterB = await DetailAsync(id, token);
        Total(afterB).Should().Be(Total(seenByA) - 300_000);
        Version(afterB).Should().NotBe(staleVersion);

        // A vẫn ở màn hình cũ.
        var stale = new (string Name, Task<HttpResponseMessage> Request)[]
        {
            ("finalize", _client.PostJsonAsync($"/api/v1/invoices/{id}/finalize", new { version = staleVersion }, token)),
            ("edit", _client.PutJsonAsync($"/api/v1/invoices/{id}/lines/{rentLine}", new { amount = 1_000_000, note = "Giảm", version = staleVersion }, token)),
            ("reset", _client.DeleteAsync($"/api/v1/invoices/{id}/lines/{LineId(afterB, "ManualDiscount")}?version={staleVersion}", token)),
            ("add", _client.PostJsonAsync($"/api/v1/invoices/{id}/manual-lines",
                new { type = "Surcharge", description = "Thay khóa", amount = 250_000, note = "Hỏng khóa", version = staleVersion }, token)),
            ("note", _client.PutJsonAsync($"/api/v1/invoices/{id}/note", new { note = "Nhắc đóng sớm", version = staleVersion }, token)),
        };
        foreach (var (name, request) in stale)
        {
            var response = await request;
            response.StatusCode.Should().Be(HttpStatusCode.Conflict, $"{name}: {await response.Content.ReadAsStringAsync()}");
            (await response.ReadProblemCodeAsync()).Should().Be("CONCURRENCY_CONFLICT", name);
        }
        var unchanged = await DetailAsync(id, token);
        unchanged.GetProperty("summary").GetProperty("status").GetString().Should().Be("Draft");
        Total(unchanged).Should().Be(Total(afterB));
        Version(unchanged).Should().Be(Version(afterB));

        // A tải lại ⇒ chốt đúng số đang thấy.
        var finalized = await _client.PostJsonAsync($"/api/v1/invoices/{id}/finalize", new { version = Version(unchanged) }, token);
        finalized.StatusCode.Should().Be(HttpStatusCode.OK, await finalized.Content.ReadAsStringAsync());
        Total(await finalized.ReadAsync<JsonElement>()).Should().Be(Total(afterB));
    }

    /// <summary>Đổi chỉ ghi chú của một dòng (không đổi tiền) cũng đổi version phiếu — người kia sửa cùng dòng đó không đè mất.</summary>
    [Fact]
    public async Task EditOnlyLineNote_ChangesInvoiceVersion()
    {
        var (token, id) = await DraftAsync();
        (await _client.AddInvoiceLineAsync(id, new { type = "ManualDiscount", description = "Mất nước", amount = 300_000, note = "Sự cố" }, token))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        var before = await DetailAsync(id, token);

        var edited = await _client.PutJsonAsync($"/api/v1/invoices/{id}/lines/{LineId(before, "ManualDiscount")}",
            new { amount = 300_000, note = "Sự cố bồn nước ngày 3/11", version = Version(before) }, token);
        edited.StatusCode.Should().Be(HttpStatusCode.OK, await edited.Content.ReadAsStringAsync());

        var after = await DetailAsync(id, token);
        Total(after).Should().Be(Total(before));
        Version(after).Should().NotBe(Version(before));
    }
}
