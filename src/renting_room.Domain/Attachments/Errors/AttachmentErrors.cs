using renting_room.Domain.Common;

namespace renting_room.Domain.Attachments;

public static class AttachmentErrors
{
    public static readonly Error NotFound = Error.NotFound("ATTACHMENT_NOT_FOUND", "Không tìm thấy tệp.");
    public static readonly Error OwnerNotFound = Error.NotFound("OWNER_NOT_FOUND", "Không tìm thấy đối tượng cần đính kèm tệp.");
    public static readonly Error NoFile = Error.Validation("NO_FILE", "Chưa chọn tệp nào.");
    public static readonly Error TooManyFiles = Error.Validation("TOO_MANY_FILES", $"Tối đa {AttachmentRules.MaxFilesPerRequest} tệp mỗi lần tải lên.");
    public static readonly Error FileTypeNotAllowed = Error.Validation("FILE_TYPE_NOT_ALLOWED", "Chỉ nhận ảnh JPEG, PNG, WEBP, HEIC hoặc PDF.");
    public static readonly Error FileTooLarge = Error.Validation("FILE_TOO_LARGE", "Mỗi tệp tối đa 10 MB.");
    public static readonly Error CategoryNotAllowed = Error.BusinessRule("CATEGORY_NOT_ALLOWED", "Loại tài liệu này không dùng được cho đối tượng đã chọn.");
    public static readonly Error TooManyAttachments = Error.BusinessRule("TOO_MANY_ATTACHMENTS", "Đã đạt số tệp tối đa cho loại tài liệu này.");
    public static readonly Error Locked = Error.BusinessRule("ATTACHMENT_LOCKED", "Tệp là chứng từ đã ghi nhận — không xóa được.");
    public static readonly Error SignedUrlNotSupported = Error.BusinessRule("SIGNED_URL_NOT_SUPPORTED",
        "Tệp này không có liên kết tạm — hãy tải qua API (tệp nhạy cảm hoặc lưu trên ổ đĩa máy chủ).");
    public static readonly Error QuotaExceeded = Error.InsufficientStorage("STORAGE_QUOTA_EXCEEDED", "Đã vượt dung lượng lưu trữ của tổ chức.");
    public static readonly Error StorageUnavailable = Error.Unavailable("STORAGE_UNAVAILABLE", "Kho lưu trữ tệp chưa sẵn sàng — thử lại sau.");
}
