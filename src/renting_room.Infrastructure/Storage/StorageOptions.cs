using renting_room.Application.Attachments;
using renting_room.Domain.Attachments;

namespace renting_room.Infrastructure.Storage;

/// <summary>
/// M09 FS-BR-12…17. Mặc định <c>Local</c> — chạy được không cần MinIO. <c>S3</c> (MinIO / AWS / nhà cung cấp trong nước) chỉ khi cấu hình đủ;
/// thiếu ⇒ app không khởi động (FS-BR-13), nhưng MinIO sập lúc đang chạy thì app vẫn chạy (FS-BR-14).
/// </summary>
public sealed class StorageOptions
{
    public const string SectionName = "Storage";

    public StorageProviderKind Provider { get; set; } = StorageProviderKind.Local;

    public long OrganizationQuotaBytes { get; set; } = AttachmentRules.DefaultOrganizationQuotaBytes;

    /// <summary>FS-BR-08: số ngày giữ file vật lý sau khi xóa mềm.</summary>
    public int PurgeAfterDays { get; set; } = 30;

    public LocalStorageOptions Local { get; set; } = new();

    public S3StorageOptions S3 { get; set; } = new();

    /// <returns>Danh sách lỗi cấu hình (rỗng = hợp lệ).</returns>
    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();
        if (OrganizationQuotaBytes <= 0)
            errors.Add("Storage:OrganizationQuotaBytes must be greater than 0.");
        if (PurgeAfterDays is < 1 or > 3650)
            errors.Add("Storage:PurgeAfterDays must be between 1 and 3650.");
        if (string.IsNullOrWhiteSpace(Local.RootPath))
            errors.Add("Storage:Local:RootPath must not be empty.");
        if (S3.SignedUrlSeconds is < 1 or > 300)
            errors.Add("Storage:S3:SignedUrlSeconds must be between 1 and 300 (signed links live at most 5 minutes).");
        if (S3.TimeoutSeconds is < 1 or > 300)
            errors.Add("Storage:S3:TimeoutSeconds must be between 1 and 300.");

        if (Provider != StorageProviderKind.S3)
            return errors;

        if (string.IsNullOrWhiteSpace(S3.ServiceUrl) || !Uri.TryCreate(S3.ServiceUrl, UriKind.Absolute, out _))
            errors.Add("Storage:S3:ServiceUrl is required (an absolute URL such as http://localhost:9000) when Storage:Provider=S3.");
        if (string.IsNullOrWhiteSpace(S3.Bucket))
            errors.Add("Storage:S3:Bucket is required when Storage:Provider=S3.");
        if (string.IsNullOrWhiteSpace(S3.AccessKey))
            errors.Add("Storage:S3:AccessKey is required when Storage:Provider=S3 (set Storage__S3__AccessKey).");
        if (string.IsNullOrWhiteSpace(S3.SecretKey))
            errors.Add("Storage:S3:SecretKey is required when Storage:Provider=S3 (set Storage__S3__SecretKey).");
        return errors;
    }
}

public sealed class LocalStorageOptions
{
    /// <summary>Thư mục lưu file, NGOÀI wwwroot. Chạy Docker: mount volume vào đây, nếu không file mất khi container bị tạo lại.</summary>
    public string RootPath { get; set; } = "App_Data/files";
}

public sealed class S3StorageOptions
{
    public string ServiceUrl { get; set; } = string.Empty;

    public string Bucket { get; set; } = "renting-room";

    public string AccessKey { get; set; } = string.Empty;

    public string SecretKey { get; set; } = string.Empty;

    /// <summary>Bắt buộc <c>true</c> với MinIO (địa chỉ dạng http://host:9000/bucket/key).</summary>
    public bool ForcePathStyle { get; set; } = true;

    public string Region { get; set; } = "us-east-1";

    public int SignedUrlSeconds { get; set; } = 300;

    public int TimeoutSeconds { get; set; } = 30;

    /// <summary>S3 đã cấu hình đủ để dùng (kể cả khi tệp mới đang lưu Local — để vẫn đọc được tệp cũ trên S3, FS-BR-15).</summary>
    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(ServiceUrl) && !string.IsNullOrWhiteSpace(AccessKey) && !string.IsNullOrWhiteSpace(SecretKey)
        && !string.IsNullOrWhiteSpace(Bucket);
}

internal sealed class StorageSettings(Microsoft.Extensions.Options.IOptions<StorageOptions> options) : IStorageSettings
{
    public long OrganizationQuotaBytes => options.Value.OrganizationQuotaBytes;

    public TimeSpan SignedUrlLifetime => TimeSpan.FromSeconds(options.Value.S3.SignedUrlSeconds);

    public TimeSpan PurgeAfter => TimeSpan.FromDays(options.Value.PurgeAfterDays);
}
