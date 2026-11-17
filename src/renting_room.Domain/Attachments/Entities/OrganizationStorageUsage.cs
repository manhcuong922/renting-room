namespace renting_room.Domain.Attachments;

/// <summary>Dung lượng đang dùng của tổ chức (FS-BR-02). Cập nhật bằng câu lệnh nguyên tử trong repository — không qua entity.</summary>
public sealed class OrganizationStorageUsage
{
    public Guid OrganizationId { get; private set; }
    public long UsedBytes { get; private set; }
}
