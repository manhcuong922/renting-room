using renting_room.Application.Common.Interfaces;
using renting_room.Infrastructure.Persistence;

namespace renting_room.Infrastructure.Auditing;

internal sealed class AuditTrail(AppDbContext db, ICurrentUser currentUser, TimeProvider clock) : IAuditTrail
{
    public void Record(string action, string entityType, Guid? entityId, object? details = null) =>
        db.AuditLogs.Add(Create(action, entityType, entityId, details));

    /// <summary>Lệnh đọc không có thay đổi nào khác trong context ⇒ SaveChanges chỉ ghi đúng dòng nhật ký này.</summary>
    public async Task RecordReadAsync(string action, string entityType, Guid? entityId, object? details, CancellationToken cancellationToken)
    {
        db.AuditLogs.Add(Create(action, entityType, entityId, details));
        await db.SaveChangesAsync(cancellationToken);
    }

    private AuditLog Create(string action, string entityType, Guid? entityId, object? details)
    {
        var actor = AuditActor.From(currentUser, clock);
        return AuditLog.Create(actor, actor.OrganizationId, action, entityType, entityId, AuditJson.Serialize(details));
    }
}
