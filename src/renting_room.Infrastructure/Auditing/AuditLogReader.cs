using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using renting_room.Application.Audit;
using renting_room.Application.Common.Interfaces;
using renting_room.Application.Common.Models;
using renting_room.Domain.Common;
using renting_room.Infrastructure.Persistence;

namespace renting_room.Infrastructure.Auditing;

/// <summary>
/// <c>audit_logs</c> không có global filter tổ chức (bảng kỹ thuật) ⇒ luôn lọc tường minh theo tổ chức của người gọi;
/// không có tổ chức (SystemAdmin) ⇒ không thấy gì (fail-closed, C-01).
/// </summary>
internal sealed class AuditLogReader(AppDbContext db, ICurrentUser currentUser) : IAuditLogReader
{
    public async Task<PagedResult<AuditLogDto>> ListAsync(AuditLogFilter filter, CancellationToken cancellationToken)
    {
        if (currentUser.OrganizationId is not { } organizationId)
            return new PagedResult<AuditLogDto>([], filter.Page, filter.PageSize, 0);

        var query = db.AuditLogs.AsNoTracking().Where(a => a.OrganizationId == organizationId);
        if (!string.IsNullOrEmpty(filter.EntityType))
            query = query.Where(a => a.EntityType == filter.EntityType);
        if (filter.EntityId is { } entityId)
            query = query.Where(a => a.EntityId == entityId);
        if (filter.UserId is { } userId)
            query = query.Where(a => a.UserId == userId);
        if (!string.IsNullOrEmpty(filter.Action))
            query = query.Where(a => a.Action == filter.Action);
        // Npgsql chỉ nhận DateTimeOffset offset 0 cho timestamptz ⇒ đổi đầu ngày giờ Việt Nam sang UTC.
        if (filter.From is { } from)
        {
            var start = StartOfBusinessDay(from);
            query = query.Where(a => a.OccurredAt >= start);
        }
        if (filter.To is { } to)
        {
            var end = StartOfBusinessDay(to.AddDays(1));
            query = query.Where(a => a.OccurredAt < end);
        }

        var total = await query.CountAsync(cancellationToken);
        var rows = await query
            .OrderByDescending(a => a.OccurredAt).ThenByDescending(a => a.Id)
            .Skip((filter.Page - 1) * filter.PageSize).Take(filter.PageSize)
            .Select(a => new
            {
                a.Id, a.OccurredAt, a.UserId, a.Action, a.EntityType, a.EntityId, a.Changes, a.IpAddress,
                UserName = db.Users.Where(u => u.Id == a.UserId).Select(u => u.FullName).FirstOrDefault()
            })
            .ToListAsync(cancellationToken);

        var items = rows.Select(r => new AuditLogDto(r.Id, r.OccurredAt, r.UserId, r.UserName, r.Action, r.EntityType, r.EntityId,
            ParseChanges(r.Changes), r.IpAddress)).ToList();
        return new PagedResult<AuditLogDto>(items, filter.Page, filter.PageSize, total);
    }

    private static DateTimeOffset StartOfBusinessDay(DateOnly date) =>
        new DateTimeOffset(date.ToDateTime(TimeOnly.MinValue), VietnamTime.Offset).ToUniversalTime();

    private static JsonElement? ParseChanges(string? json)
    {
        if (json is null)
            return null;
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }
}
