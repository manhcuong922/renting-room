using renting_room.Domain.Common;

namespace renting_room.Domain.Attachments;

/// <summary>Metadata một tệp đính kèm + vị trí lưu (M09). Nội dung nằm ở <see cref="StorageProvider"/> — Local hoặc S3 (MinIO).</summary>
public sealed class Attachment : TenantEntity
{
    private Attachment() { } // EF Core

    public AttachmentOwnerType OwnerType { get; private set; }
    public Guid OwnerId { get; private set; }
    public AttachmentCategory Category { get; private set; }
    public string OriginalFileName { get; private set; } = null!;
    public string ContentType { get; private set; } = null!;
    public long SizeBytes { get; private set; }
    public StorageProviderKind StorageProvider { get; private set; }
    public string StorageKey { get; private set; } = null!;
    public string Sha256 { get; private set; } = null!;
    public bool IsSensitive { get; private set; }
    public string? Description { get; private set; }
    public DateTimeOffset UploadedAt { get; private set; }
    public Guid? UploadedBy { get; private set; }
    public DateTimeOffset? DeletedAt { get; private set; }
    public Guid? DeletedBy { get; private set; }

    /// <summary>Đã xóa vật lý (job dọn / ẩn danh) — metadata còn để đối soát dung lượng.</summary>
    public DateTimeOffset? PurgedAt { get; private set; }

    public bool IsDeleted => DeletedAt is not null;

    public static Attachment Create(
        Guid id, Guid organizationId, AttachmentOwnerType ownerType, Guid ownerId, AttachmentCategory category, string originalFileName,
        string contentType, long sizeBytes, StorageProviderKind provider, string storageKey, string sha256, string? description,
        DateTimeOffset now, Guid? uploadedBy)
    {
        var attachment = new Attachment
        {
            Id = id, OwnerType = ownerType, OwnerId = ownerId, Category = category,
            OriginalFileName = AttachmentRules.SanitizeFileName(originalFileName), ContentType = contentType, SizeBytes = sizeBytes,
            StorageProvider = provider, StorageKey = storageKey, Sha256 = sha256, IsSensitive = AttachmentRules.IsSensitive(category),
            Description = TextNormalizer.TrimToNull(description), UploadedAt = now, UploadedBy = uploadedBy
        };
        attachment.AssignOrganization(organizationId);
        return attachment;
    }

    public void SoftDelete(DateTimeOffset now, Guid? userId)
    {
        DeletedAt ??= now;
        DeletedBy ??= userId;
    }

    public void MarkPurged(DateTimeOffset now) => PurgedAt ??= now;
}
