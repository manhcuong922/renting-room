using System.Text.Json;
using System.Text.Json.Nodes;

namespace renting_room.IntegrationTests.Infrastructure;

/// <summary>
/// Thao tác trên một phiếu nháp kèm <c>version</c> đang thấy (M07 §10, 05/11/2026) — như frontend: mở phiếu (GET) rồi gửi lại version của lần mở đó.
/// Test cố ý gửi version cũ thì gọi thẳng endpoint.
/// </summary>
public static class InvoiceDraftApi
{
    /// <summary>Version của phiếu; không đọc được (404…) ⇒ "0" để lệnh vẫn đi tới và trả đúng lỗi của nó.</summary>
    public static async Task<string> InvoiceVersionAsync(this HttpClient client, Guid invoiceId, string token)
    {
        var response = await client.GetAsync($"/api/v1/invoices/{invoiceId}", token);
        if (!response.IsSuccessStatusCode)
            return "0";
        return (await response.ReadAsync<JsonElement>()).GetProperty("summary").GetProperty("version").GetString()!;
    }

    private static JsonObject WithVersion(object? body, string version)
    {
        var node = body is null ? new JsonObject() : JsonSerializer.SerializeToNode(body, TestApi.Json)!.AsObject();
        node["version"] = version;
        return node;
    }

    public static async Task<HttpResponseMessage> FinalizeInvoiceAsync(this HttpClient client, Guid invoiceId, string token) =>
        await client.PostJsonAsync($"/api/v1/invoices/{invoiceId}/finalize", new { version = await client.InvoiceVersionAsync(invoiceId, token) }, token);

    public static async Task<HttpResponseMessage> EditInvoiceLineAsync(this HttpClient client, Guid invoiceId, Guid lineId, object body, string token) =>
        await client.PutJsonAsync($"/api/v1/invoices/{invoiceId}/lines/{lineId}", WithVersion(body, await client.InvoiceVersionAsync(invoiceId, token)), token);

    public static async Task<HttpResponseMessage> ResetInvoiceLineAsync(this HttpClient client, Guid invoiceId, Guid lineId, string token) =>
        await client.DeleteAsync($"/api/v1/invoices/{invoiceId}/lines/{lineId}?version={await client.InvoiceVersionAsync(invoiceId, token)}", token);

    public static async Task<HttpResponseMessage> AddInvoiceLineAsync(this HttpClient client, Guid invoiceId, object body, string token) =>
        await client.PostJsonAsync($"/api/v1/invoices/{invoiceId}/manual-lines", WithVersion(body, await client.InvoiceVersionAsync(invoiceId, token)), token);

    public static async Task<HttpResponseMessage> UpdateInvoiceNoteAsync(this HttpClient client, Guid invoiceId, string? note, string token) =>
        await client.PutJsonAsync($"/api/v1/invoices/{invoiceId}/note", new { note, version = await client.InvoiceVersionAsync(invoiceId, token) }, token);
}
