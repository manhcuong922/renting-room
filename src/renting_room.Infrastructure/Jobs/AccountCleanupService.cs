using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using renting_room.Application.Identity;
using renting_room.Application.Identity.Members;

namespace renting_room.Infrastructure.Jobs;

/// <summary>M01: mỗi 6 giờ xóa refresh token đã hết hạn quá 7 ngày và nhật ký đăng nhập cũ hơn 180 ngày để 2 bảng không phình mãi.</summary>
public sealed class AccountCleanupService(IServiceScopeFactory scopeFactory, TimeProvider clock, ILogger<AccountCleanupService> logger)
    : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromHours(6);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval, clock);
        do
        {
            try
            {
                await RunAsync(clock.GetUtcNow(), stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Account cleanup failed");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    /// <returns>Số refresh token và số dòng nhật ký đăng nhập đã xóa.</returns>
    public async Task<(int Tokens, int LoginEvents)> RunAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var purged = await AccountCleanup.PurgeAsync(
            scope.ServiceProvider.GetRequiredService<IRefreshTokenRepository>(), scope.ServiceProvider.GetRequiredService<ILoginEventRepository>(),
            now, cancellationToken);
        if (purged.Tokens > 0 || purged.LoginEvents > 0)
            logger.LogInformation("Account cleanup: deleted {Tokens} expired refresh token(s), {Events} old login event(s)", purged.Tokens, purged.LoginEvents);
        return purged;
    }
}
