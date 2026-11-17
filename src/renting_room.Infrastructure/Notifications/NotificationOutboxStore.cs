using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;
using renting_room.Application.Notifications;
using renting_room.Domain.Notifications;
using renting_room.Infrastructure.Persistence;

namespace renting_room.Infrastructure.Notifications;

public sealed class ClaimedNotification
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }
    public string Channel { get; set; } = null!;
}

/// <summary>
/// Một trong HAI chỗ duy nhất dùng SQL thô xuyên tổ chức (M11 §6): dispatcher nhận việc. Sau đó mọi thao tác chạy trong scope của đúng một tổ chức.
/// </summary>
public sealed class NotificationOutboxStore(AppDbContext db, TimeProvider clock)
{
    /// <summary>
    /// <c>UPDATE … WHERE id IN (SELECT … FOR UPDATE SKIP LOCKED) RETURNING</c>: nhận tối đa <paramref name="batch"/> tin và chuyển <c>Sending</c> trong
    /// cùng câu lệnh ⇒ hai dispatcher không bao giờ nhận cùng một tin. Tin <c>Sending</c> hết lease (app chết giữa chừng) được nhận lại.
    /// </summary>
    /// <param name="inWindow">Trong khung giờ gửi (NT-BR-08). Ngoài khung chỉ <c>ManualResend</c> được nhận.</param>
    public async Task<IReadOnlyList<ClaimedNotification>> ClaimAsync(
        int batch, bool inWindow, IReadOnlyCollection<NotificationChannel> channels, TimeSpan lease, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        // Hết lease mà đã dùng đủ lượt ⇒ thất bại (không nhận lại — CHECK attempts ≤ 5).
        await db.Database.ExecuteSqlRawAsync(
            """
            UPDATE notification_outbox SET status = 'Failed', last_error = 'LEASE_EXPIRED', locked_until = NULL
            WHERE status = 'Sending' AND locked_until < @now AND attempts >= @max
            """,
            [new NpgsqlParameter("now", now), new NpgsqlParameter("max", (short)RetryPolicy.MaxAttempts)], cancellationToken);

        return await db.Database.SqlQueryRaw<ClaimedNotification>(
            """
            UPDATE notification_outbox SET status = 'Sending', attempts = attempts + 1, locked_until = @lease
            WHERE id IN (
                SELECT id FROM notification_outbox
                WHERE ((status = 'Pending' AND next_attempt_at <= @now) OR (status = 'Sending' AND locked_until < @now AND attempts < @max))
                  AND channel = ANY(@channels) AND (kind = 'ManualResend' OR @in_window)
                ORDER BY next_attempt_at
                LIMIT @batch
                FOR UPDATE SKIP LOCKED)
            RETURNING id, organization_id, channel
            """,
            new NpgsqlParameter("lease", now + lease),
            new NpgsqlParameter("now", now),
            new NpgsqlParameter("max", (short)RetryPolicy.MaxAttempts),
            new NpgsqlParameter("batch", batch),
            new NpgsqlParameter("in_window", inWindow),
            new NpgsqlParameter("channels", NpgsqlDbType.Array | NpgsqlDbType.Varchar) { Value = channels.Select(c => c.ToString()).ToArray() })
            .ToListAsync(cancellationToken);
    }

    /// <summary>NT-BR-18: tin ở trạng thái kết thúc quá <paramref name="retention"/> ⇒ xóa.</summary>
    public Task<int> DeleteOldAsync(TimeSpan retention, CancellationToken cancellationToken) =>
        db.NotificationOutbox.IgnoreQueryFilters()
            .Where(n => n.CreatedAt < clock.GetUtcNow() - retention
                && (n.Status == NotificationStatus.Sent || n.Status == NotificationStatus.Failed
                    || n.Status == NotificationStatus.Skipped || n.Status == NotificationStatus.Cancelled))
            .ExecuteDeleteAsync(cancellationToken);
}

/// <summary>Chỗ thứ hai xuyên tổ chức: bot tra mã liên kết theo hash và hồ sơ theo chat id.</summary>
internal sealed class ZaloLinkLookup(AppDbContext db) : IZaloLinkLookup
{
    public async Task<ZaloLinkTarget?> FindUsableAsync(string codeHash, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var code = await db.ZaloLinkCodes.IgnoreQueryFilters().AsNoTracking()
            .Where(c => c.CodeHash == codeHash && c.UsedAt == null && c.RevokedAt == null && c.ExpiresAt > now)
            .Select(c => new ZaloLinkTarget(c.Id, c.OrganizationId, c.RenterId))
            .FirstOrDefaultAsync(cancellationToken);
        return code;
    }

    public async Task<bool> TryConsumeAsync(Guid codeId, DateTimeOffset now, CancellationToken cancellationToken) =>
        await db.ZaloLinkCodes.IgnoreQueryFilters()
            .Where(c => c.Id == codeId && c.UsedAt == null && c.RevokedAt == null && c.ExpiresAt > now)
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.UsedAt, now), cancellationToken) == 1;

    public async Task<IReadOnlyList<ZaloChatRenter>> ListByChatAsync(string chatId, CancellationToken cancellationToken) =>
        await db.Renters.IgnoreQueryFilters().AsNoTracking().Where(r => r.ZaloChatId == chatId)
            .Select(r => new ZaloChatRenter(r.OrganizationId, r.Id)).ToListAsync(cancellationToken);
}
