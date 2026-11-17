namespace renting_room.Domain.Attachments;

/// <summary>M09 §3: ánh xạ OwnerType → Category (FS-BR-03), nhạy cảm, giới hạn số lượng, khóa xóa (FS-BR-08).</summary>
public static class AttachmentRules
{
    public const long MaxFileBytes = 10 * 1024 * 1024;
    public const int MaxFilesPerRequest = 20;
    public const long DefaultOrganizationQuotaBytes = 5L * 1024 * 1024 * 1024;

    private static readonly Dictionary<AttachmentOwnerType, AttachmentCategory[]> Allowed = new()
    {
        [AttachmentOwnerType.Renter] = [AttachmentCategory.IdCardFront, AttachmentCategory.IdCardBack, AttachmentCategory.Portrait, AttachmentCategory.Other],
        [AttachmentOwnerType.Room] = [AttachmentCategory.RoomPhoto, AttachmentCategory.Other],
        [AttachmentOwnerType.Property] =
            [AttachmentCategory.AuthorizationDoc, AttachmentCategory.LessorIdCard, AttachmentCategory.ComplianceDoc, AttachmentCategory.Other],
        [AttachmentOwnerType.MeterReading] = [AttachmentCategory.MeterPhoto, AttachmentCategory.Other],
        [AttachmentOwnerType.Payment] = [AttachmentCategory.PaymentProof, AttachmentCategory.Other],
        // ⏸ 16/11/2026: chưa mở cho hợp đồng.
        [AttachmentOwnerType.Contract] = []
    };

    private static readonly HashSet<AttachmentCategory> Sensitive =
    [
        AttachmentCategory.IdCardFront, AttachmentCategory.IdCardBack, AttachmentCategory.LessorIdCard, AttachmentCategory.Portrait,
        AttachmentCategory.ResidenceConfirmation, AttachmentCategory.ContractScan, AttachmentCategory.AuthorizationDoc
    ];

    /// <summary>Mỗi owner chỉ giữ 1 tệp hiệu lực cho các category này — upload mới thay tệp cũ.</summary>
    private static readonly HashSet<AttachmentCategory> SingleActive =
        [AttachmentCategory.IdCardFront, AttachmentCategory.IdCardBack, AttachmentCategory.Portrait, AttachmentCategory.LessorIdCard];

    private static readonly Dictionary<AttachmentCategory, int> MaxPerOwner = new()
    {
        [AttachmentCategory.RoomPhoto] = 20,
        [AttachmentCategory.MeterPhoto] = 3,
        [AttachmentCategory.PaymentProof] = 5
    };

    /// <summary>Chứng cứ tài chính: không cho người dùng xóa (FS-BR-08, bản P1 giản lược — không dò trạng thái chốt từng chỉ số).</summary>
    private static readonly HashSet<AttachmentCategory> Locked = [AttachmentCategory.PaymentProof, AttachmentCategory.MeterPhoto];

    public static bool IsAllowed(AttachmentOwnerType owner, AttachmentCategory category) =>
        Allowed.TryGetValue(owner, out var categories) && categories.Contains(category);

    public static bool IsSensitive(AttachmentCategory category) => Sensitive.Contains(category);

    public static bool ReplacesPrevious(AttachmentCategory category) => SingleActive.Contains(category);

    /// <returns>Số tệp tối đa mỗi owner + category; null = không giới hạn riêng.</returns>
    public static int? MaxPerOwnerCategory(AttachmentCategory category) => MaxPerOwner.TryGetValue(category, out var max) ? max : null;

    public static bool IsDeletionLocked(AttachmentCategory category) => Locked.Contains(category);

    /// <summary>FS-BR-05: khóa lưu trữ không chứa tên file người dùng.</summary>
    public static string StorageKey(Guid organizationId, DateTimeOffset now, Guid attachmentId) =>
        $"{organizationId:N}/{now.UtcDateTime:yyyy}/{now.UtcDateTime:MM}/{attachmentId:N}";

    /// <summary>Làm sạch tên file gốc: bỏ ký tự điều khiển và <c>/ \ : * ? " &lt; &gt; |</c>, tối đa 255.</summary>
    public static string SanitizeFileName(string? name)
    {
        var fileName = Path.GetFileName((name ?? string.Empty).Replace('\\', '/'));
        var cleaned = new string(fileName.Where(c => !char.IsControl(c) && "/\\:*?\"<>|".IndexOf(c) < 0).ToArray()).Trim();
        if (cleaned.Length == 0)
            cleaned = "tep";
        return cleaned.Length <= 255 ? cleaned : cleaned[..255];
    }
}
