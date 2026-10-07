using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using renting_room.Infrastructure.Persistence;

namespace renting_room.Infrastructure.Auditing;

/// <summary>
/// Ghi nền audit của thao tác đọc: có bản ghi đầu tiên thì mở cửa sổ gom, ghi khi đủ <see cref="AuditOptions.MaxBatchSize"/>
/// HOẶC hết <see cref="AuditOptions.FlushInterval"/> (cái nào tới trước) — mỗi lô 1 lần INSERT. Hàng đợi rỗng ⇒ ngủ, không tốn gì.
/// </summary>
internal sealed class AuditLogWriter(
    AuditQueue queue,
    IServiceScopeFactory scopeFactory,
    IOptions<AuditOptions> options,
    ILogger<AuditLogWriter> logger)
    : BackgroundService
{
    private readonly AuditOptions _options = options.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var batch = new List<AuditLog>(_options.MaxBatchSize);
        try
        {
            while (await queue.Reader.WaitToReadAsync(stoppingToken))
            {
                await FillBatchAsync(batch, stoppingToken);
                await FlushAsync(batch);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // App đang tắt.
        }
        finally
        {
            // Tắt bình thường (deploy, restart): đóng hàng đợi rồi vét nốt trước khi dừng.
            queue.Complete();
            while (queue.Reader.TryRead(out var log))
                batch.Add(log);
            await FlushAsync(batch);
        }
    }

    private async Task FillBatchAsync(List<AuditLog> batch, CancellationToken stoppingToken)
    {
        // Đồng hồ thật (không dùng TimeProvider nghiệp vụ): đây là nhịp kỹ thuật, đồng hồ giả trong test sẽ làm lô không bao giờ đóng.
        using var window = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        window.CancelAfter(_options.FlushInterval);
        try
        {
            while (batch.Count < _options.MaxBatchSize)
            {
                if (queue.Reader.TryRead(out var log))
                {
                    batch.Add(log);
                    continue;
                }
                if (!await queue.Reader.WaitToReadAsync(window.Token))
                    return;
            }
        }
        catch (OperationCanceledException) when (!stoppingToken.IsCancellationRequested)
        {
            // Hết cửa sổ gom ⇒ ghi những gì đang có.
        }
    }

    /// <summary>Không truyền stoppingToken: lô đã lấy ra khỏi hàng đợi phải được ghi trọn kể cả khi app đang tắt.</summary>
    private async Task FlushAsync(List<AuditLog> batch)
    {
        if (batch.Count == 0)
            return;
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.AuditLogs.AddRange(batch);
            await db.SaveChangesAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            // Không để mất dấu: chép tóm tắt từng bản ghi ra log ứng dụng.
            logger.LogError(ex, "Failed to write {Count} audit log(s): {Entries}", batch.Count,
                string.Join("; ", batch.Select(l => $"{l.Action} {l.EntityType} {l.EntityId} by {l.UserId} at {l.OccurredAt:O}")));
        }
        finally
        {
            batch.Clear();
        }
    }
}
