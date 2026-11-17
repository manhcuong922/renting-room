using Mediator;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using renting_room.Application.Notifications;
using renting_room.Domain.Common;
using renting_room.Infrastructure.Notifications;
using renting_room.Infrastructure.Persistence;

namespace renting_room.Infrastructure.Jobs;

/// <summary>
/// NT-BR-06: mỗi giờ tạo tin nhắc nợ cho phiếu quá hạn của khu đã cài mốc. Lần lượt từng tổ chức (mẫu <see cref="RenterAnonymizationService"/>),
/// lỗi một tổ chức không chặn tổ chức khác.
/// </summary>
public sealed class OverdueReminderScheduler(
    IServiceScopeFactory scopeFactory, TimeProvider clock, IOptions<NotificationOptions> options, ILogger<OverdueReminderScheduler> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var settings = options.Value;
        if (!settings.ReminderSchedulerEnabled)
            return;

        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(settings.ReminderIntervalMinutes), clock);
        do
        {
            await RunAsync(clock.GetUtcNow().ToBusinessDate(), null, stoppingToken);
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    /// <param name="organizationIds">Giới hạn tổ chức (test); null = mọi tổ chức.</param>
    /// <returns>Số tin đã tạo.</returns>
    public async Task<int> RunAsync(DateOnly asOf, IReadOnlyCollection<Guid>? organizationIds, CancellationToken cancellationToken)
    {
        List<Guid> organizations;
        try
        {
            organizations = organizationIds?.ToList() ?? await ListOrganizationsAsync(scopeFactory, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Overdue reminders: failed to list organizations");
            return 0;
        }

        var total = 0;
        foreach (var organizationId in organizations)
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                scope.ServiceProvider.GetRequiredService<CurrentUserOverride>().RunAs(new OrganizationSystemUser(organizationId));
                total += await scope.ServiceProvider.GetRequiredService<ISender>().Send(new EnqueueOverdueRemindersCommand(asOf), cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Overdue reminders failed for organization {OrganizationId}", organizationId);
            }
        }
        return total;
    }

    internal static async Task<List<Guid>> ListOrganizationsAsync(IServiceScopeFactory scopeFactory, CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().Organizations.AsNoTracking().Select(o => o.Id).ToListAsync(cancellationToken);
    }
}

/// <summary>NT-BR-18: mỗi ngày xóa tin ở trạng thái kết thúc quá <c>Notifications:RetentionDays</c> (mặc định 180).</summary>
public sealed class NotificationCleanupService(
    IServiceScopeFactory scopeFactory, TimeProvider clock, IOptions<NotificationOptions> options, ILogger<NotificationCleanupService> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromDays(1), clock);
        do
        {
            try
            {
                await RunAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Notification cleanup failed");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    public async Task<int> RunAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<NotificationOutboxStore>()
            .DeleteOldAsync(TimeSpan.FromDays(options.Value.RetentionDays), cancellationToken);
    }
}
