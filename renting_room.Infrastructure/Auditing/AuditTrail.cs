using renting_room.Application.Common.Interfaces;
using renting_room.Infrastructure.Persistence;

namespace renting_room.Infrastructure.Auditing;

internal sealed class AuditTrail(AppDbContext db, AuditQueue queue, ICurrentUser currentUser, TimeProvider clock) : IAuditTrail
{
    public void Record(string action, string entityType, Guid? entityId, object? details = null) =>
        db.AuditLogs.Add(Create(action, entityType, entityId, details));

    public void RecordRead(string action, string entityType, Guid? entityId, object? details = null) =>
        queue.Enqueue(Create(action, entityType, entityId, details));

    private AuditLog Create(string action, string entityType, Guid? entityId, object? details)
    {
        var actor = AuditActor.From(currentUser, clock);
        return AuditLog.Create(actor, actor.OrganizationId, action, entityType, entityId, AuditJson.Serialize(details));
    }
}
