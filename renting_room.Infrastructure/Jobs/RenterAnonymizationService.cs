using Mediator;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using renting_room.Application.Renters;
using renting_room.Domain.Common;
using renting_room.Infrastructure.Persistence;

namespace renting_room.Infrastructure.Jobs;

/// <summary>
/// RT-BR-06: mỗi ngày ẩn danh hồ sơ người thuê rời đi quá thời gian giữ của tổ chức (mặc định 36 tháng, tổ chức tắt thì bỏ qua).
/// Lần lượt từng tổ chức, mỗi tổ chức một scope chạy dưới danh nghĩa hệ thống của tổ chức đó; lỗi 1 tổ chức không chặn tổ chức khác.
/// </summary>
public sealed class RenterAnonymizationService(
    IServiceScopeFactory scopeFactory,
    TimeProvider clock,
    ILogger<RenterAnonymizationService> logger)
    : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromDays(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval, clock);
        do
        {
            await RunAsync(clock.GetUtcNow().ToBusinessDate(), null, stoppingToken);
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    /// <param name="organizationIds">Giới hạn tổ chức (test); null = mọi tổ chức.</param>
    /// <returns>Số hồ sơ đã ẩn danh.</returns>
    public async Task<int> RunAsync(DateOnly asOf, IReadOnlyCollection<Guid>? organizationIds, CancellationToken cancellationToken)
    {
        List<Guid> organizations;
        try
        {
            organizations = organizationIds?.ToList() ?? await ListOrganizationsAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Renter anonymization: failed to list organizations");
            return 0;
        }

        var total = 0;
        foreach (var organizationId in organizations)
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                scope.ServiceProvider.GetRequiredService<CurrentUserOverride>().RunAs(new OrganizationSystemUser(organizationId));
                total += await scope.ServiceProvider.GetRequiredService<ISender>()
                    .Send(new AnonymizeExpiredRentersCommand(asOf), cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Renter anonymization failed for organization {OrganizationId}", organizationId);
            }
        }
        if (total > 0)
            logger.LogInformation("Anonymized {Count} renter(s) past their organization's retention period", total);
        return total;
    }

    private async Task<List<Guid>> ListOrganizationsAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.Organizations.AsNoTracking().Select(o => o.Id).ToListAsync(cancellationToken);
    }
}
