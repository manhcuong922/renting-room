using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using renting_room.Application.Notifications;

namespace renting_room.Infrastructure.Notifications;

/// <summary>
/// Long-poll <c>getUpdates</c> ⇒ <see cref="ZaloBotMessageHandler"/> ⇒ bot trả lời. Không cần domain / HTTPS (M11 quyết định 12/10/2026).
/// Chưa có token ⇒ dịch vụ không chạy.
/// </summary>
public sealed class ZaloBotUpdatePoller(
    IZaloBotClient client, ZaloBotMessageHandler handler, IOptions<ZaloBotOptions> options, ILogger<ZaloBotUpdatePoller> logger)
    : BackgroundService
{
    private static readonly TimeSpan MinBackoff = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan MaxBackoff = TimeSpan.FromMinutes(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var settings = options.Value;
        if (!client.IsConfigured || !settings.PollerEnabled)
        {
            logger.LogInformation("Zalo bot poller not started (token configured: {Configured}, enabled: {Enabled})", client.IsConfigured, settings.PollerEnabled);
            return;
        }

        long? offset = null;
        var backoff = MinBackoff;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var result = await client.GetUpdatesAsync(offset, settings.PollTimeoutSeconds, stoppingToken);
                if (!result.Ok)
                {
                    await Task.Delay(result.RetryAfter ?? backoff, stoppingToken);
                    backoff = TimeSpan.FromTicks(Math.Min(backoff.Ticks * 2, MaxBackoff.Ticks));
                    continue;
                }
                backoff = MinBackoff;
                offset = await ProcessAsync(result.Updates, offset, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Zalo bot poll round failed");
                await Task.Delay(backoff, stoppingToken);
            }
        }
    }

    /// <summary>Xử lý từng tin, trả <c>offset</c> kế tiếp. Public để test gọi không cần vòng lặp.</summary>
    public async Task<long?> ProcessAsync(IReadOnlyList<ZaloUpdate> updates, long? offset, CancellationToken cancellationToken)
    {
        foreach (var update in updates)
        {
            if (update.UpdateId is { } id)
                offset = Math.Max(offset ?? 0, id + 1);
            // Xử lý mã liên kết là idempotent: update cũ trả lại sau khi khởi động lại ⇒ bot báo "mã đã dùng" (§10).
            if (await handler.HandleAsync(update, cancellationToken) is { } reply)
                await client.SendMessageAsync(update.ChatId, reply, cancellationToken);
        }
        return offset;
    }
}
