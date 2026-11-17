using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using renting_room.Application.Notifications;

namespace renting_room.Infrastructure.Notifications;

/// <summary>
/// Gọi Zalo Bot Platform: <c>POST {ApiBaseUrl}/bot{token}/{method}</c> với JSON, trả <c>{ "ok": true, "result": … }</c> hoặc
/// <c>{ "ok": false, "error_code", "description" }</c>. LƯU Ý: dạng phản hồi viết theo tài liệu công khai, CHƯA chạy thử với token thật (spike NT-T00) —
/// toàn bộ việc đọc phản hồi nằm trong lớp này nên chỉ sửa ở đây nếu Zalo khác đi.
/// URL chứa token ⇒ không ghi URL / ngoại lệ vào log (chỉ tên phương thức và loại lỗi).
/// </summary>
public sealed class ZaloBotClient(IHttpClientFactory httpFactory, IOptions<ZaloBotOptions> options, ILogger<ZaloBotClient> logger) : IZaloBotClient
{
    public const string HttpClientName = "zalo-bot";

    private string? _botName;

    public bool IsConfigured => options.Value.IsConfigured;

    public async Task<string?> GetBotNameAsync(CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(options.Value.BotName))
            return options.Value.BotName;
        if (_botName is not null || !IsConfigured)
            return _botName;

        var response = await CallAsync("getMe", new { }, cancellationToken);
        if (response.Body is { } body && body.TryGetProperty("result", out var result))
            _botName = Text(result, "display_name") ?? Text(result, "name") ?? Text(result, "username");
        return _botName;
    }

    public async Task<ZaloUpdatesResult> GetUpdatesAsync(long? offset, int timeoutSeconds, CancellationToken cancellationToken)
    {
        var response = await CallAsync("getUpdates", new { timeout = timeoutSeconds, offset }, cancellationToken);
        if (response.Failure is { } failure)
            return new ZaloUpdatesResult(false, [], failure.ErrorCode, failure.RetryAfter);
        if (response.Body is not { } body || !body.TryGetProperty("result", out var result))
            return new ZaloUpdatesResult(true, []);

        var updates = new List<ZaloUpdate>();
        foreach (var item in result.ValueKind == JsonValueKind.Array ? result.EnumerateArray().ToList() : [result])
        {
            if (ParseUpdate(item) is { } update)
                updates.Add(update);
        }
        return new ZaloUpdatesResult(true, updates);
    }

    public async Task<SendResult> SendMessageAsync(string chatId, string text, CancellationToken cancellationToken)
    {
        var response = await CallAsync("sendMessage", new { chat_id = chatId, text }, cancellationToken);
        if (response.Failure is { } failure)
            return failure;
        var messageId = response.Body is { } body && body.TryGetProperty("result", out var result) ? Text(result, "message_id") : null;
        return SendResult.Ok(messageId);
    }

    private static ZaloUpdate? ParseUpdate(JsonElement item)
    {
        if (!item.TryGetProperty("message", out var message))
            return null;
        string? chatId = null;
        if (message.TryGetProperty("chat", out var chat))
            chatId = Text(chat, "id");
        chatId ??= message.TryGetProperty("from", out var from) ? Text(from, "id") : null;
        if (chatId is null)
            return null;
        long? updateId = item.TryGetProperty("update_id", out var id) && id.TryGetInt64(out var parsed) ? parsed : null;
        return new ZaloUpdate(updateId, chatId, Text(message, "text"));
    }

    private static string? Text(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value)
            ? value.ValueKind switch
            {
                JsonValueKind.String => value.GetString(),
                JsonValueKind.Number => value.GetRawText(),
                _ => null
            }
            : null;

    private sealed record Response(JsonElement? Body, SendResult? Failure);

    /// <summary>Không ném lỗi: mọi sự cố thành <see cref="SendResult"/> (nhóm lỗi theo NT-BR-09 / NT-BR-10).</summary>
    private async Task<Response> CallAsync(string method, object payload, CancellationToken cancellationToken)
    {
        var settings = options.Value;
        if (!settings.IsConfigured)
            return new Response(null, SendResult.Permanent("ZALO_NOT_CONFIGURED"));

        try
        {
            using var http = httpFactory.CreateClient(HttpClientName);
            using var reply = await http.PostAsJsonAsync($"{settings.ApiBaseUrl.TrimEnd('/')}/bot{settings.Token}/{method}", payload, cancellationToken);
            var retryAfter = ReadRetryAfter(reply);
            using var document = await ReadJsonAsync(reply, cancellationToken);
            var root = document?.RootElement;
            var ok = reply.IsSuccessStatusCode && (root is not { } r || !r.TryGetProperty("ok", out var flag) || flag.ValueKind != JsonValueKind.False);
            if (ok)
                return new Response(root?.Clone(), null);

            var description = root is { } body ? Text(body, "description") : null;
            return new Response(null, Classify(reply.StatusCode, description, retryAfter));
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException && !cancellationToken.IsCancellationRequested)
        {
            // Không ghi ex (có thể chứa URL ⇒ token) — chỉ loại lỗi.
            logger.LogWarning("Zalo {Method} failed: {Error}", method, ex.GetType().Name);
            return new Response(null, SendResult.Transient("NETWORK"));
        }
    }

    private static async Task<JsonDocument?> ReadJsonAsync(HttpResponseMessage reply, CancellationToken cancellationToken)
    {
        try
        {
            return await JsonDocument.ParseAsync(await reply.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static TimeSpan? ReadRetryAfter(HttpResponseMessage reply) =>
        reply.Headers.RetryAfter?.Delta ?? (reply.Headers.TryGetValues("Retry-After", out var values)
            && double.TryParse(values.FirstOrDefault(), NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds)
            ? TimeSpan.FromSeconds(seconds) : null);

    /// <summary>429 / 5xx ⇒ lỗi tạm; 403 hoặc "chat không tồn tại" ⇒ người nhận không tới được; còn lại ⇒ vĩnh viễn.</summary>
    internal static SendResult Classify(HttpStatusCode status, string? description, TimeSpan? retryAfter)
    {
        if (status == HttpStatusCode.TooManyRequests)
            return SendResult.Transient("RATE_LIMITED", retryAfter);
        if ((int)status >= 500)
            return SendResult.Transient($"HTTP_{(int)status}");
        if (status == HttpStatusCode.Forbidden
            || (description?.Contains("chat not found", StringComparison.OrdinalIgnoreCase) ?? false)
            || (description?.Contains("blocked", StringComparison.OrdinalIgnoreCase) ?? false))
            return SendResult.Unreachable($"HTTP_{(int)status}");
        return SendResult.Permanent($"HTTP_{(int)status}");
    }
}
