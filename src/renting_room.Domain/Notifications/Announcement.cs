using renting_room.Domain.Common;

namespace renting_room.Domain.Notifications;

/// <summary>
/// M11 §17 (NT-UC-12): thông báo chung chủ trọ / quản lý khu tự soạn (tăng giá điện, cắt nước…) gửi cho người đứng tên các phòng của MỘT khu qua Zalo.
/// Nội dung lưu ở đây — không có phiếu để dựng lại (NT-BR-23). Mỗi người nhận = một dòng <see cref="NotificationOutbox"/> kind <c>Announcement</c>.
/// </summary>
public sealed class Announcement : TenantEntity, IPropertyScoped
{
    public const int TitleMaxLength = 100;
    public const int BodyMaxLength = 1500;

    private Announcement() { } // EF Core

    public Guid PropertyId { get; private set; }
    public string Title { get; private set; } = null!;
    public string Body { get; private set; } = null!;

    /// <summary><c>true</c> = gửi mọi phòng đang thuê của khu lúc bấm gửi; <c>false</c> = các phòng chọn.</summary>
    public bool AllRooms { get; private set; }

    /// <summary>Số phòng có người đứng tên (HĐ đang hiệu lực) trong phạm vi gửi.</summary>
    public int RoomCount { get; private set; }

    /// <summary>Số tin đã xếp hàng — sau khi gộp các hồ sơ cùng một chat Zalo (NT-BR-20).</summary>
    public int RecipientCount { get; private set; }

    /// <exception cref="ArgumentException">Tiêu đề / nội dung rỗng hoặc quá dài (NT-BR-21) — validator đã chặn trước ở Application.</exception>
    public static Announcement Create(Guid propertyId, string title, string body, bool allRooms, int roomCount, int recipientCount)
    {
        var trimmedTitle = Require(title, TitleMaxLength, nameof(title));
        var trimmedBody = Require(body, BodyMaxLength, nameof(body));
        ArgumentOutOfRangeException.ThrowIfNegative(roomCount);
        ArgumentOutOfRangeException.ThrowIfNegative(recipientCount);
        return new Announcement
        {
            Id = Guid.CreateVersion7(), PropertyId = propertyId, Title = trimmedTitle, Body = trimmedBody, AllRooms = allRooms,
            RoomCount = roomCount, RecipientCount = recipientCount
        };
    }

    private static string Require(string value, int maxLength, string name)
    {
        var trimmed = value?.Trim() ?? string.Empty;
        if (trimmed.Length == 0 || trimmed.Length > maxLength)
            throw new ArgumentException($"{name} must be 1–{maxLength} characters.", name);
        return trimmed;
    }
}
