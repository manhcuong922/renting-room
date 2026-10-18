using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using renting_room.Infrastructure.Auditing;
using renting_room.Infrastructure.Persistence;

namespace renting_room.Infrastructure.Jobs;

/// <summary>
/// S2 (README C-10): mỗi ngày xóa dòng <c>audit_logs</c> cũ hơn <see cref="AuditOptions.RetentionYears"/> năm (mặc định 5) — theo từng tổ chức
/// (dùng index <c>(organization_id, occurred_at)</c>) và theo lô để không khóa bảng lâu. Không đụng dữ liệu nghiệp vụ.
/// </summary>
public sealed class AuditRetentionService(
    IServiceScopeFactory scopeFactory,
    TimeProvider clock,
    IOptions<AuditOptions> options,
    ILogger<AuditRetentionService> logger)
    : BackgroundService
{
    private const int BatchSize = 5_000;
    private static readonly TimeSpan Interval = TimeSpan.FromDays(1);

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
                logger.LogError(ex, "Audit retention: purge failed");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    /// <returns>Số dòng nhật ký đã xóa.</returns>
    public async Task<int> RunAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        var cutoff = now.AddYears(-options.Value.RetentionYears);
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var organizations = await db.Organizations.AsNoTracking().Select(o => (Guid?)o.Id).ToListAsync(cancellationToken);

        var total = 0;
        foreach (var organizationId in organizations.Append(null))
        {
            int deleted;
            do
            {
                deleted = organizationId is { } id
                    ? await db.Database.ExecuteSqlAsync($"""
                        DELETE FROM audit_logs WHERE id IN (
                            SELECT id FROM audit_logs WHERE organization_id = {id} AND occurred_at < {cutoff} LIMIT {BatchSize})
                        """, cancellationToken)
                    : await db.Database.ExecuteSqlAsync($"""
                        DELETE FROM audit_logs WHERE id IN (
                            SELECT id FROM audit_logs WHERE organization_id IS NULL AND occurred_at < {cutoff} LIMIT {BatchSize})
                        """, cancellationToken);
                total += deleted;
            }
            while (deleted == BatchSize);
        }
        if (total > 0)
            logger.LogInformation("Audit retention: deleted {Count} audit log row(s) older than {Cutoff:yyyy-MM-dd}", total, cutoff);
        return total;
    }
}
