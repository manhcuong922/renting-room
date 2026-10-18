using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using renting_room.Application.Common.Interfaces;

namespace renting_room.Infrastructure.Idempotency;

/// <summary>Xóa định kỳ các idempotency key đã hết hạn để bảng không phình mãi.</summary>
internal sealed class IdempotencyCleanupService(
    IServiceScopeFactory scopeFactory,
    TimeProvider clock,
    ILogger<IdempotencyCleanupService> logger)
    : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromHours(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval, clock);
        do
        {
            await PurgeAsync(stoppingToken);
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task PurgeAsync(CancellationToken stoppingToken)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var deleted = await scope.ServiceProvider.GetRequiredService<IIdempotencyStore>().PurgeExpiredAsync(stoppingToken);
            if (deleted > 0)
                logger.LogInformation("Purged {Count} expired idempotency key(s)", deleted);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Failed to purge expired idempotency keys");
        }
    }
}
