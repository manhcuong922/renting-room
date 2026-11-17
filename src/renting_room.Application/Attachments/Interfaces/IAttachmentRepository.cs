using renting_room.Domain.Attachments;

namespace renting_room.Application.Attachments;

public interface IAttachmentRepository
{
    void Add(Attachment attachment);

    /// <summary>Tệp chưa xóa (đang hiệu lực) — null nếu không có hoặc thuộc tổ chức khác.</summary>
    Task<Attachment?> GetActiveForUpdateAsync(Guid id, CancellationToken cancellationToken);

    Task<IReadOnlyList<Attachment>> ListActiveByOwnerAsync(AttachmentOwnerType ownerType, Guid ownerId, CancellationToken cancellationToken);

    /// <summary>Tệp hiệu lực (có theo dõi để sửa) của owner theo category.</summary>
    Task<IReadOnlyList<Attachment>> ListActiveForUpdateAsync(
        AttachmentOwnerType ownerType, Guid ownerId, AttachmentCategory? category, CancellationToken cancellationToken);

    /// <summary>Tệp nhạy cảm của owner còn file vật lý (kể cả đã xóa mềm) — để xóa hẳn khi ẩn danh (FS-BR-09).</summary>
    Task<IReadOnlyList<Attachment>> ListUnpurgedSensitiveForUpdateAsync(AttachmentOwnerType ownerType, Guid ownerId, CancellationToken cancellationToken);

    /// <summary>Tệp đã xóa mềm trước <paramref name="deletedBefore"/> mà file vật lý còn (có theo dõi để sửa), cũ nhất trước (FS-BR-08).</summary>
    Task<IReadOnlyList<Attachment>> ListPurgeableForUpdateAsync(DateTimeOffset deletedBefore, int take, CancellationToken cancellationToken);
}

/// <summary>Hạn mức dung lượng theo tổ chức (FS-BR-02), cập nhật nguyên tử trong transaction hiện tại.</summary>
public interface IStorageQuota
{
    /// <returns>false nếu cộng thêm <paramref name="bytes"/> vượt <paramref name="quotaBytes"/>; không đổi gì.</returns>
    Task<bool> TryReserveAsync(long bytes, long quotaBytes, CancellationToken cancellationToken);

    Task ReleaseAsync(long bytes, CancellationToken cancellationToken);
}

/// <summary>Đối tượng được đính kèm có tồn tại — và nhìn thấy được — với người dùng hiện tại không (FS-BR-04; tổ chức, phạm vi khu).</summary>
public interface IAttachmentOwners
{
    Task<bool> ExistsAsync(AttachmentOwnerType ownerType, Guid ownerId, CancellationToken cancellationToken);
}
