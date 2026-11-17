using renting_room.Domain.Attachments;

namespace renting_room.Application.Attachments;

public sealed record AttachmentDto(
    Guid Id, AttachmentOwnerType OwnerType, Guid OwnerId, AttachmentCategory Category, string FileName, string ContentType, long SizeBytes,
    bool IsSensitive, string? Description, DateTimeOffset UploadedAt)
{
    public static AttachmentDto From(Attachment a) =>
        new(a.Id, a.OwnerType, a.OwnerId, a.Category, a.OriginalFileName, a.ContentType, a.SizeBytes, a.IsSensitive, a.Description, a.UploadedAt);
}

public sealed record AttachmentListDto(IReadOnlyList<AttachmentDto> Items);

/// <summary>Tệp đang nhận từ request — <see cref="Open"/> trả luồng mới mỗi lần gọi (đọc đầu để nhận diện, rồi đọc lại để lưu).</summary>
public sealed record UploadedFile(string FileName, long Length, Func<Stream> Open);

/// <summary>Nội dung tải về; người gọi sở hữu và phải đóng <see cref="Content"/>.</summary>
public sealed record AttachmentContent(Stream Content, string FileName, string ContentType, long SizeBytes, bool IsSensitive);

public sealed record SignedUrlDto(string Url, DateTimeOffset ExpiresAt);
