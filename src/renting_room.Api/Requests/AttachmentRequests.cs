using renting_room.Domain.Attachments;

namespace renting_room.Requests;

/// <summary>Form multipart của <c>POST /attachments</c> — tên field khớp thuộc tính (ownerType, ownerId, category, description, files).</summary>
public sealed class UploadAttachmentsForm
{
    public AttachmentOwnerType OwnerType { get; init; }

    public Guid OwnerId { get; init; }

    public AttachmentCategory Category { get; init; }

    public string? Description { get; init; }

    public List<IFormFile> Files { get; init; } = [];
}
