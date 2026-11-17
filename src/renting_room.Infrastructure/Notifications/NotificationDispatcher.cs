using System.Diagnostics;
using Mediator;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using renting_room.Application.Notifications;
using renting_room.Domain.Common;
using renting_room.Domain.Notifications;
using renting_room.Infrastructure.Jobs;

namespace renting_room.Infrastructure.Notifications;

/// <summary>
/// Nhận tin từ hàng đợi <c>notification_outbox</c> (2 giây / lần), gửi từng tin trong scope của đúng tổ chức chứa tin. Khung giờ 08:00–20:00 giờ VN
/// (NT-BR-08), token bucket mỗi kênh (NT-BR-11), 429 kèm <c>retry_after</c> ⇒ tạm dừng kênh đó (§10). Lỗi một tin không chặn tin khác.
/// </summary>
public sealed class NotificationDispatcher(
    IServiceScopeFactory scopeFactory, TimeProvider clock, IOptions<NotificationOptions> options, ILogger<NotificationDispatcher> logger)
    : BackgroundService
{
    private readonly Dictionary<NotificationChannel, DateTimeOffset> _pausedUntil = [];
    private readonly Dictionary<NotificationChannel, long> _lastSendTicks = [];

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var settings = options.Value;
        if (!settings.DispatcherEnabled)
            return;

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(settings.DispatchIntervalSeconds), clock);
        do
        {
            try
            {
                while (await RunOnceAsync(stoppingToken) >= settings.BatchSize)
                {
                    // còn việc ⇒ nhận tiếp ngay, không chờ tick
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Notification dispatch round failed");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    /// <summary>Nhận và gửi một lượt. Trả số tin đã nhận.</summary>
    /// <param name="windowAt">Chỉ để test: thời điểm dùng xét khung giờ gửi (mặc định = bây giờ) — test không phải nhảy đồng hồ dùng chung.</param>
    public async Task<int> RunOnceAsync(CancellationToken cancellationToken, DateTimeOffset? windowAt = null)
    {
        var settings = options.Value;
        var now = clock.GetUtcNow();
        var channels = Enum.GetValues<NotificationChannel>().Where(c => !_pausedUntil.TryGetValue(c, out var until) || until <= now).ToList();
        if (channels.Count == 0)
            return 0;

        IReadOnlyList<ClaimedNotification> claimed;
        await using (var scope = scopeFactory.CreateAsyncScope())
        {
            claimed = await scope.ServiceProvider.GetRequiredService<NotificationOutboxStore>()
                .ClaimAsync(settings.BatchSize, InSendWindow(windowAt ?? now, settings), channels, TimeSpan.FromMinutes(settings.LeaseMinutes), cancellationToken);
        }

        foreach (var item in claimed)
        {
            var channel = Enum.Parse<NotificationChannel>(item.Channel);
            if (_pausedUntil.TryGetValue(channel, out var until) && until > clock.GetUtcNow())
                continue; // tin đã chuyển Sending; hết lease sẽ được nhận lại — kênh đang bị 429 nên không gửi tiếp
            await ThrottleAsync(channel, settings, cancellationToken);
            await SendOneAsync(item, channel, cancellationToken);
        }
        return claimed.Count;
    }

    private async Task SendOneAsync(ClaimedNotification item, NotificationChannel channel, CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            scope.ServiceProvider.GetRequiredService<CurrentUserOverride>().RunAs(new OrganizationSystemUser(item.OrganizationId));
            var result = await scope.ServiceProvider.GetRequiredService<ISender>().Send(new DispatchNotificationCommand(item.Id), cancellationToken);
            if (result is { Outcome: SendOutcome.Transient, RetryAfter: { } retryAfter })
                _pausedUntil[channel] = clock.GetUtcNow() + retryAfter;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Tin giữ trạng thái Sending tới khi lease hết hạn rồi được nhận lại (không mất tin).
            logger.LogError(ex, "Dispatching notification {NotificationId} failed", item.Id);
        }
    }

    /// <summary>NT-BR-08: [Start, End) giờ Việt Nam.</summary>
    private static bool InSendWindow(DateTimeOffset now, NotificationOptions settings)
    {
        var hour = now.ToOffset(VietnamTime.Offset).Hour;
        return hour >= settings.SendWindowStartHour && hour < settings.SendWindowEndHour;
    }

    /// <summary>NT-BR-11: giãn cách tối thiểu giữa hai tin cùng kênh (thời gian thật, không phụ thuộc đồng hồ giả của test).</summary>
    private async Task ThrottleAsync(NotificationChannel channel, NotificationOptions settings, CancellationToken cancellationToken)
    {
        var perSecond = channel == NotificationChannel.Zalo ? settings.ZaloPerSecond : settings.EmailPerSecond;
        var interval = TimeSpan.FromSeconds(1 / perSecond);
        if (_lastSendTicks.TryGetValue(channel, out var last))
        {
            var wait = interval - Stopwatch.GetElapsedTime(last);
            if (wait > TimeSpan.Zero)
                await Task.Delay(wait, cancellationToken);
        }
        _lastSendTicks[channel] = Stopwatch.GetTimestamp();
    }
}
