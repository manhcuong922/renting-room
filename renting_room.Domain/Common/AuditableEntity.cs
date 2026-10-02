namespace renting_room.Domain.Common;

/// <summary>
/// Entity có thông tin tạo/sửa (gán tự động khi SaveChanges) và phiên bản cho optimistic concurrency
/// (ánh xạ cột hệ thống <c>xmin</c> của PostgreSQL).
/// </summary>
public abstract class AuditableEntity : Entity
{
    public DateTimeOffset CreatedAt { get; private set; }
    public Guid? CreatedBy { get; private set; }
    public DateTimeOffset? UpdatedAt { get; private set; }
    public Guid? UpdatedBy { get; private set; }
    public uint Version { get; private set; }

    public void MarkCreated(DateTimeOffset now, Guid? userId)
    {
        CreatedAt = now;
        CreatedBy = userId;
    }

    public void MarkUpdated(DateTimeOffset now, Guid? userId)
    {
        UpdatedAt = now;
        UpdatedBy = userId;
    }
}
