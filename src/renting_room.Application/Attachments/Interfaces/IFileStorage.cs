using renting_room.Domain.Attachments;

namespace renting_room.Application.Attachments;

/// <summary>Kho lưu nội dung tệp (M09 FS-BR-12…16). Hiện thực: ổ đĩa máy chủ (Local) hoặc MinIO / S3.</summary>
public interface IFileStorage
{
    StorageProviderKind Kind { get; }

    /// <exception cref="StorageUnavailableException">Kho không kết nối / từ chối được (MinIO sập, sai khóa, đĩa lỗi).</exception>
    Task SaveAsync(string key, Stream content, string contentType, CancellationToken cancellationToken);

    /// <exception cref="FileNotFoundException">Tệp không còn trong kho.</exception>
    /// <exception cref="StorageUnavailableException"/>
    Task<Stream> OpenReadAsync(string key, CancellationToken cancellationToken);

    /// <summary>Xóa tệp; tệp không tồn tại coi như đã xóa.</summary>
    Task DeleteAsync(string key, CancellationToken cancellationToken);

    /// <returns>Liên kết tạm có hạn; null nếu kho không hỗ trợ (Local) — FS-BR-16.</returns>
    string? GetSignedUrl(string key, TimeSpan lifetime);

    /// <summary>Kiểm tra kho dùng được (health check, FS-BR-14). Với S3: tạo bucket private nếu chưa có.</summary>
    /// <exception cref="StorageUnavailableException"/>
    Task CheckAvailableAsync(CancellationToken cancellationToken);
}

/// <summary>Chọn kho: tệp mới theo <c>Storage:Provider</c>, tệp cũ theo nơi đã lưu (FS-BR-15).</summary>
public interface IFileStorageSelector
{
    IFileStorage ForNewFiles { get; }

    /// <exception cref="StorageUnavailableException">Kho đó chưa được cấu hình.</exception>
    IFileStorage For(StorageProviderKind kind);
}

public interface IStorageSettings
{
    long OrganizationQuotaBytes { get; }

    TimeSpan SignedUrlLifetime { get; }

    /// <summary>FS-BR-08: tệp xóa mềm quá thời gian này thì xóa file vật lý.</summary>
    TimeSpan PurgeAfter { get; }
}

public sealed class StorageUnavailableException(string message, Exception? inner = null) : Exception(message, inner);
