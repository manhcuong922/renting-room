using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using renting_room.Application.Common.Interfaces;
using renting_room.Errors;
using renting_room.Infrastructure.Idempotency;

namespace renting_room.Idempotency;

/// <summary>Đánh dấu endpoint hỗ trợ / bắt buộc header <c>Idempotency-Key</c>.</summary>
public sealed record IdempotentMetadata(bool IsRequired);

public static partial class IdempotencyEndpointExtensions
{
    public const string HeaderName = "Idempotency-Key";
    public const string ReplayedHeaderName = "Idempotent-Replayed";

    /// <summary>
    /// <paramref name="required"/> = true: thiếu header → 400. Dùng cho thao tác tạo mới / tài chính, nơi gửi trùng gây hậu quả.
    /// </summary>
    public static TBuilder WithIdempotency<TBuilder>(this TBuilder builder, bool required = true)
        where TBuilder : IEndpointConventionBuilder =>
        builder.WithMetadata(new IdempotentMetadata(required));

    [GeneratedRegex("^[A-Za-z0-9_-]{8,64}$")]
    internal static partial Regex KeyFormat();
}

/// <summary>
/// C-08: cùng user + cùng Idempotency-Key ⇒ thao tác chỉ chạy MỘT lần.
/// <list type="bullet">
/// <item>Lần đầu: chạy bình thường, lưu response (mã hóa).</item>
/// <item>Gửi lại sau khi xong: trả đúng response cũ + header <c>Idempotent-Replayed: true</c>.</item>
/// <item>Gửi lại khi lần đầu còn đang chạy (double-click, retry song song): 409, client thử lại sau.</item>
/// <item>Cùng key nhưng nội dung khác: 422 (lỗi phía client).</item>
/// <item>Lần đầu lỗi hệ thống (5xx / exception): key bị xóa để client retry được.</item>
/// </list>
/// Đặt SAU Authentication/Authorization: request bị từ chối quyền không chiếm key.
/// </summary>
internal sealed class IdempotencyMiddleware(RequestDelegate next, ILogger<IdempotencyMiddleware> logger)
{
    public async Task InvokeAsync(
        HttpContext context, IIdempotencyStore store, ICurrentUser currentUser, IOptions<IdempotencyOptions> options)
    {
        var metadata = context.GetEndpoint()?.Metadata.GetMetadata<IdempotentMetadata>();
        if (metadata is null || HttpMethods.IsGet(context.Request.Method) || HttpMethods.IsHead(context.Request.Method))
        {
            await next(context);
            return;
        }

        var key = context.Request.Headers[IdempotencyEndpointExtensions.HeaderName].ToString();
        if (string.IsNullOrEmpty(key))
        {
            if (metadata.IsRequired)
            {
                await ProblemResponses.WriteAsync(context, StatusCodes.Status400BadRequest, "IDEMPOTENCY_KEY_REQUIRED",
                    $"Thao tác này yêu cầu header {IdempotencyEndpointExtensions.HeaderName} (khuyến nghị dùng UUID mới cho mỗi thao tác).");
                return;
            }

            await next(context);
            return;
        }

        if (!IdempotencyEndpointExtensions.KeyFormat().IsMatch(key))
        {
            await ProblemResponses.WriteAsync(context, StatusCodes.Status400BadRequest, "INVALID_IDEMPOTENCY_KEY",
                $"{IdempotencyEndpointExtensions.HeaderName} phải gồm 8–64 ký tự chữ, số, '-' hoặc '_'.");
            return;
        }

        if (currentUser.UserId is not { } userId)
        {
            await next(context); // endpoint ẩn danh: không có phạm vi user để lưu key
            return;
        }

        var request = new IdempotencyRequest(
            userId, key, await ComputeRequestHashAsync(context.Request), context.Request.Method, context.Request.Path);

        var begin = await store.TryBeginAsync(request, context.RequestAborted);
        switch (begin.Outcome)
        {
            case IdempotencyOutcome.Started:
                await ExecuteAndStoreAsync(context, store, request, options.Value.MaxStoredResponseBytes);
                return;

            case IdempotencyOutcome.Replay:
                await ReplayAsync(context, begin.Response!);
                return;

            case IdempotencyOutcome.InProgress:
                context.Response.Headers.RetryAfter = "1";
                await ProblemResponses.WriteAsync(context, StatusCodes.Status409Conflict, "IDEMPOTENCY_REQUEST_IN_PROGRESS",
                    "Yêu cầu với cùng Idempotency-Key đang được xử lý. Vui lòng thử lại sau giây lát.");
                return;

            case IdempotencyOutcome.KeyReusedWithDifferentRequest:
                await ProblemResponses.WriteAsync(context, StatusCodes.Status422UnprocessableEntity, "IDEMPOTENCY_KEY_REUSED",
                    "Idempotency-Key này đã được dùng cho một yêu cầu có nội dung khác. Hãy tạo key mới cho thao tác mới.");
                return;

            default:
                await ProblemResponses.WriteAsync(context, StatusCodes.Status409Conflict, "IDEMPOTENCY_REPLAY_UNAVAILABLE",
                    "Yêu cầu này đã được xử lý trước đó nhưng không thể trả lại kết quả cũ. Vui lòng kiểm tra lại dữ liệu.");
                return;
        }
    }

    private async Task ExecuteAndStoreAsync(HttpContext context, IIdempotencyStore store, IdempotencyRequest request, int maxStoredBytes)
    {
        var originalBody = context.Response.Body;
        await using var buffer = new MemoryStream();
        context.Response.Body = buffer;

        try
        {
            await next(context);
        }
        catch
        {
            // Lỗi hệ thống: giải phóng key để client retry. CancellationToken.None: vẫn dọn dẹp dù client đã ngắt kết nối.
            await store.AbandonAsync(request.UserId, request.Key, CancellationToken.None);
            throw;
        }
        finally
        {
            context.Response.Body = originalBody;
        }

        var status = context.Response.StatusCode;
        if (status >= StatusCodes.Status500InternalServerError)
        {
            await store.AbandonAsync(request.UserId, request.Key, CancellationToken.None);
        }
        else if (buffer.Length > maxStoredBytes)
        {
            logger.LogWarning("Response of {Path} is {Size} bytes, larger than idempotency limit; key not stored",
                request.Path, buffer.Length);
            await store.AbandonAsync(request.UserId, request.Key, CancellationToken.None);
        }
        else
        {
            var stored = new StoredResponse(
                status, context.Response.ContentType, context.Response.Headers.Location.ToString() is { Length: > 0 } location ? location : null,
                buffer.ToArray());
            await store.CompleteAsync(request.UserId, request.Key, stored, CancellationToken.None);
        }

        buffer.Position = 0;
        await buffer.CopyToAsync(originalBody, context.RequestAborted);
    }

    private static async Task ReplayAsync(HttpContext context, StoredResponse response)
    {
        context.Response.StatusCode = response.StatusCode;
        context.Response.Headers[IdempotencyEndpointExtensions.ReplayedHeaderName] = "true";
        context.Response.Headers.CacheControl = "no-store";
        if (response.ContentType is not null)
            context.Response.ContentType = response.ContentType;
        if (response.Location is not null)
            context.Response.Headers.Location = response.Location;

        await context.Response.Body.WriteAsync(response.Body, context.RequestAborted);
    }

    /// <summary>SHA-256 của method + path + query + body. Body được buffer để model binding vẫn đọc lại được.</summary>
    private static async Task<string> ComputeRequestHashAsync(HttpRequest request)
    {
        request.EnableBuffering();
        using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        sha.AppendData(Encoding.UTF8.GetBytes($"{request.Method}\n{request.Path}{request.QueryString}\n"));

        var chunk = new byte[8192];
        int read;
        while ((read = await request.Body.ReadAsync(chunk, request.HttpContext.RequestAborted)) > 0)
            sha.AppendData(chunk, 0, read);

        request.Body.Position = 0;
        return Convert.ToHexString(sha.GetHashAndReset()).ToLowerInvariant();
    }
}
