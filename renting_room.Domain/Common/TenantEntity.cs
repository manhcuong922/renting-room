namespace renting_room.Domain.Common;

/// <summary>Đánh dấu dữ liệu thuộc về một tổ chức (chủ trọ) — được cô lập bởi global query filter (C-01).</summary>
public interface ITenantEntity
{
    Guid OrganizationId { get; }
    void AssignOrganization(Guid organizationId);
}

public abstract class TenantEntity : AuditableEntity, ITenantEntity
{
    public Guid OrganizationId { get; private set; }

    public void AssignOrganization(Guid organizationId)
    {
        if (organizationId == Guid.Empty)
            throw new ArgumentException("Organization id is required.", nameof(organizationId));
        if (OrganizationId != Guid.Empty && OrganizationId != organizationId)
            throw new InvalidOperationException("Entity already belongs to another organization.");

        OrganizationId = organizationId;
    }
}
