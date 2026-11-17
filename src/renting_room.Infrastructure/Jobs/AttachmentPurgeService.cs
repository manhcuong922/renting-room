using Mediator;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using renting_room.Application.Attachments;
using renting_room.Domain.Common;
using renting_room.Infrastructure.Persistence;

namespace renting_room.Infrastructure.Jobs;

/// <summary>
/// FS-BR-08 / FS-11: mỗi ngày xóa file vật lý của tệp xóa mềm quá <c>Storage:PurgeAfterDays</c> và trả hạn mức dung lượng.
/// Lần lượt từng tổ chức (mẫu <see cref="RenterAnonymizationService"/>); lỗi một tổ chức không chặn tổ chức khác.
/// </summary>
public sealed class AttachmentPurgeService(
    IServiceScopeFactory scopeFactory, TimeProvider clock, ILogger<AttachmentPurgeService> logger)
    : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromDays(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval, clock);
        do
        {
            await RunAsync(clock.GetUtcNow(), null, stoppingToken);
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    /// <param name="organizationIds">Giới hạn tổ chức (test); null = mọi tổ chức.</param>
    /// <returns>Số tệp đã dọn.</returns>
    public async Task<int> RunAsync(DateTimeOffset now, IReadOnlyCollection<Guid>? organizationIds, CancellationToken cancellationToken)
    {
        List<Guid> organizations;
        try
        {
            organizations = organizationIds?.ToList() ?? await ListOrganizationsAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Attachment purge: failed to list organizations");
            return 0;
        }

        var total = 0;
        foreach (var organizationId in organizations)
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                scope.ServiceProvider.GetRequiredService<CurrentUserOverride>().RunAs(new OrganizationSystemUser(organizationId));
                total += await scope.ServiceProvider.GetRequiredService<ISender>().Send(new PurgeDeletedAttachmentsCommand(now), cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Attachment purge failed for organization {OrganizationId}", organizationId);
            }
        }
        if (total > 0)
            logger.LogInformation("Purged {Count} soft-deleted attachment file(s)", total);
        return total;
    }

    private async Task<List<Guid>> ListOrganizationsAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().Organizations.AsNoTracking().Select(o => o.Id).ToListAsync(cancellationToken);
    }
}
