using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using renting_room.Application.Common.Interfaces;
using renting_room.Domain.Common;
using renting_room.Domain.Identity;

namespace renting_room.Infrastructure.Auditing;

/// <summary>
/// Đọc ChangeTracker ngay trước SaveChanges và sinh dòng audit cho mỗi entity được tạo / sửa / xóa.
/// Dòng audit được thêm vào chính lần lưu đó ⇒ cùng batch lệnh, cùng transaction, không thêm round-trip.
/// </summary>
internal static class EntityChangeAudit
{
    private sealed record ValueChange(object? Old, object? New);

    public static List<AuditLog> Collect(ChangeTracker changeTracker, AuditActor actor)
    {
        var logs = new List<AuditLog>();
        foreach (var entry in changeTracker.Entries())
        {
            if (!AuditPolicy.IsAudited(entry.Metadata.ClrType))
                continue;

            var (action, changes) = entry.State switch
            {
                EntityState.Added => (AuditActions.Created, Snapshot(entry, p => p.CurrentValue)),
                EntityState.Deleted => (AuditActions.Deleted, Snapshot(entry, p => p.OriginalValue)),
                EntityState.Modified => (AuditActions.Updated, Diff(entry)),
                _ => (null, null)
            };
            // Sửa mà chỉ đổi cột hệ thống (UpdatedAt, Version…) ⇒ không có gì đáng ghi.
            if (action is null || changes!.Count == 0)
                continue;

            logs.Add(AuditLog.Create(actor, OrganizationOf(entry.Entity, actor.OrganizationId), action,
                entry.Metadata.ClrType.Name, ((Entity)entry.Entity).Id, AuditJson.Serialize(changes)));
        }
        return logs;
    }

    private static Dictionary<string, object?> Snapshot(EntityEntry entry, Func<PropertyEntry, object?> valueOf) =>
        entry.Properties
            .Where(p => !AuditPolicy.IsIgnored(p.Metadata.Name))
            .ToDictionary(p => p.Metadata.Name, p => Redact(p, valueOf(p)));

    private static Dictionary<string, object?> Diff(EntityEntry entry)
    {
        var changes = new Dictionary<string, object?>();
        foreach (var property in entry.Properties)
        {
            if (!property.IsModified || AuditPolicy.IsIgnored(property.Metadata.Name)
                || property.Metadata.GetValueComparer().Equals(property.OriginalValue, property.CurrentValue))
                continue;

            changes[property.Metadata.Name] = IsSensitive(property)
                ? AuditPolicy.RedactedValue
                : new ValueChange(property.OriginalValue, property.CurrentValue);
        }
        return changes;
    }

    private static object? Redact(PropertyEntry property, object? value) =>
        value is not null && IsSensitive(property) ? AuditPolicy.RedactedValue : value;

    private static bool IsSensitive(PropertyEntry property) =>
        AuditPolicy.IsSensitive(property.Metadata.Name, property.Metadata.ClrType);

    private static Guid? OrganizationOf(object entity, Guid? fallback) => entity switch
    {
        ITenantEntity tenant => tenant.OrganizationId,
        Organization organization => organization.Id,
        User user => user.OrganizationId,
        _ => fallback
    };
}

internal static class AuditJson
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        DictionaryKeyPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() }
    };

    public static string? Serialize(object? value) => value is null ? null : JsonSerializer.Serialize(value, Options);
}
