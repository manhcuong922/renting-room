using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using renting_room.Application.Common.Interfaces;
using renting_room.Infrastructure.Persistence;

namespace renting_room.Infrastructure.Idempotency;

/// <summary>
/// Lưu trạng thái idempotency trong PostgreSQL. Không dùng transaction của request: bản ghi "InProgress" phải được
/// commit NGAY để request song song cùng key nhìn thấy và bị chặn.
/// </summary>
public sealed class IdempotencyStore(
    AppDbContext db,
    IDataProtectionProvider dataProtection,
    IOptions<IdempotencyOptions> options,
    TimeProvider clock,
    ILogger<IdempotencyStore> logger)
    : IIdempotencyStore
{
    private const int MaxBeginAttempts = 3;
    private readonly IDataProtector _protector = dataProtection.CreateProtector("renting_room.Idempotency.v1");

    public async Task<IdempotencyBeginResult> TryBeginAsync(IdempotencyRequest request, CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < MaxBeginAttempts; attempt++)
        {
            var now = clock.GetUtcNow();
            if (await TryInsertAsync(request, now, cancellationToken))
                return new IdempotencyBeginResult(IdempotencyOutcome.Started);

            var existing = await db.IdempotencyRecords
                .AsNoTracking()
                .FirstOrDefaultAsync(r => r.UserId == request.UserId && r.Key == request.Key, cancellationToken);

            if (existing is null)
                continue; // vừa bị xóa (hết hạn / abandon) giữa 2 câu lệnh → thử chèn lại

            if (IsExpiredOrAbandoned(existing, now))
            {
                await DeleteIfExpiredOrAbandonedAsync(request, now, cancellationToken);
                continue;
            }

            if (existing.RequestHash != request.RequestHash)
                return new IdempotencyBeginResult(IdempotencyOutcome.KeyReusedWithDifferentRequest);

            if (existing.Status == IdempotencyStatus.InProgress)
                return new IdempotencyBeginResult(IdempotencyOutcome.InProgress);

            return Replay(existing);
        }

        // Tranh chấp liên tục với request khác cùng key — an toàn nhất là báo đang xử lý.
        return new IdempotencyBeginResult(IdempotencyOutcome.InProgress);
    }

    public Task CompleteAsync(Guid userId, string key, StoredResponse response, CancellationToken cancellationToken)
    {
        var protectedBody = _protector.Protect(response.Body);
        var completedAt = clock.GetUtcNow();

        return db.IdempotencyRecords
            .Where(r => r.UserId == userId && r.Key == key && r.Status == IdempotencyStatus.InProgress)
            .ExecuteUpdateAsync(s => s
                .SetProperty(r => r.Status, IdempotencyStatus.Completed)
                .SetProperty(r => r.ResponseStatusCode, response.StatusCode)
                .SetProperty(r => r.ResponseContentType, response.ContentType)
                .SetProperty(r => r.ResponseLocation, response.Location)
                .SetProperty(r => r.ResponseBody, protectedBody)
                .SetProperty(r => r.CompletedAt, completedAt), cancellationToken);
    }

    public Task AbandonAsync(Guid userId, string key, CancellationToken cancellationToken) =>
        db.IdempotencyRecords
            .Where(r => r.UserId == userId && r.Key == key && r.Status == IdempotencyStatus.InProgress)
            .ExecuteDeleteAsync(cancellationToken);

    public Task<int> PurgeExpiredAsync(CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        return db.IdempotencyRecords.Where(r => r.ExpiresAt <= now).ExecuteDeleteAsync(cancellationToken);
    }

    /// <summary>INSERT … ON CONFLICT DO NOTHING: nguyên tử, không ném lỗi trùng khóa; trả về true nếu chèn được.</summary>
    private async Task<bool> TryInsertAsync(IdempotencyRequest request, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var expiresAt = now.AddHours(options.Value.RetentionHours);

        var inserted = await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO idempotency_keys (user_id, key, request_hash, method, path, status, created_at, expires_at)
            VALUES ({request.UserId}, {request.Key}, {request.RequestHash}, {request.Method}, {request.Path},
                    {IdempotencyStatus.InProgress}, {now}, {expiresAt})
            ON CONFLICT (user_id, key) DO NOTHING
            """, cancellationToken);

        return inserted == 1;
    }

    private bool IsExpiredOrAbandoned(IdempotencyRecord record, DateTimeOffset now) =>
        record.ExpiresAt <= now
        || (record.Status == IdempotencyStatus.InProgress
            && record.CreatedAt <= now.AddSeconds(-options.Value.InProgressTimeoutSeconds));

    private Task DeleteIfExpiredOrAbandonedAsync(IdempotencyRequest request, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var staleBefore = now.AddSeconds(-options.Value.InProgressTimeoutSeconds);

        // Điều kiện lặp lại trong WHERE: không xóa nhầm bản ghi mà request khác vừa tạo lại.
        return db.IdempotencyRecords
            .Where(r => r.UserId == request.UserId && r.Key == request.Key
                && (r.ExpiresAt <= now || (r.Status == IdempotencyStatus.InProgress && r.CreatedAt <= staleBefore)))
            .ExecuteDeleteAsync(cancellationToken);
    }

    private IdempotencyBeginResult Replay(IdempotencyRecord record)
    {
        try
        {
            var body = record.ResponseBody is null ? [] : _protector.Unprotect(record.ResponseBody);
            var response = new StoredResponse(record.ResponseStatusCode!.Value, record.ResponseContentType, record.ResponseLocation, body);
            return new IdempotencyBeginResult(IdempotencyOutcome.Replay, response);
        }
        catch (CryptographicException ex)
        {
            logger.LogError(ex, "Cannot decrypt stored idempotent response for user {UserId}", record.UserId);
            return new IdempotencyBeginResult(IdempotencyOutcome.ReplayUnavailable);
        }
    }
}
