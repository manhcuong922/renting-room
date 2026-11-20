using renting_room.Domain.Common;

namespace renting_room.Domain.Notifications;

/// <summary>
/// M11: một dòng = một tin tới một người trên một kênh. Hàng đợi bền trong PostgreSQL (không Redis / RabbitMQ). Nội dung KHÔNG lưu —
/// dựng lại từ phiếu lúc gửi (NT-BR-17), hoặc từ <see cref="Announcement"/> với thông báo chung (§17). <see cref="Recipient"/> là ảnh chụp chat id / email lúc tạo tin, xóa khi ẩn danh (NT-BR-16).
/// </summary>
public sealed class NotificationOutbox : TenantEntity
{
    private NotificationOutbox() { } // EF Core

    /// <summary>Phiếu của tin; null với thông báo chung (§17). Đúng một trong <see cref="InvoiceId"/> / <see cref="AnnouncementId"/> có giá trị (DB CHECK).</summary>
    public Guid? InvoiceId { get; private set; }

    public Guid? AnnouncementId { get; private set; }
    public Guid RenterId { get; private set; }
    public NotificationChannel Channel { get; private set; }
    public NotificationKind Kind { get; private set; }

    /// <summary><c>InvoiceIssued</c> = 0 · <c>OverdueReminder</c> = số ngày quá hạn đã cài · <c>ManualResend</c> = số thứ tự lần gửi lại.</summary>
    public int Step { get; private set; }

    public string? Recipient { get; private set; }
    public NotificationStatus Status { get; private set; }
    public int Attempts { get; private set; }
    public DateTimeOffset NextAttemptAt { get; private set; }
    public DateTimeOffset? LockedUntil { get; private set; }
    public string? LastError { get; private set; }
    public string? ProviderMessageId { get; private set; }
    public DateTimeOffset? SentAt { get; private set; }

    public bool IsTerminal => Status is NotificationStatus.Sent or NotificationStatus.Failed or NotificationStatus.Skipped or NotificationStatus.Cancelled;

    public static NotificationOutbox Create(
        Guid invoiceId, Guid renterId, NotificationChannel channel, NotificationKind kind, int step, string? recipient, DateTimeOffset now) =>
        new()
        {
            Id = Guid.CreateVersion7(), InvoiceId = invoiceId, RenterId = renterId, Channel = channel, Kind = kind, Step = step,
            Recipient = recipient, Status = NotificationStatus.Pending, NextAttemptAt = now
        };

    /// <summary>NT-BR-20 / NT-BR-23: thông báo chung chỉ đi Zalo, bước 0, không gắn phiếu.</summary>
    public static NotificationOutbox CreateForAnnouncement(Guid announcementId, Guid renterId, string recipient, DateTimeOffset now) =>
        new()
        {
            Id = Guid.CreateVersion7(), AnnouncementId = announcementId, RenterId = renterId, Channel = NotificationChannel.Zalo,
            Kind = NotificationKind.Announcement, Step = 0, Recipient = recipient, Status = NotificationStatus.Pending, NextAttemptAt = now
        };

    /// <summary>Pending → Sending: dispatcher nhận tin, giữ lease để app chết giữa chừng thì tin quay lại.</summary>
    public void MarkSending(DateTimeOffset now, TimeSpan lease)
    {
        EnsureStatus(NotificationStatus.Pending);
        Status = NotificationStatus.Sending;
        Attempts++;
        LockedUntil = now + lease;
    }

    public void MarkSent(DateTimeOffset now, string? providerMessageId)
    {
        EnsureStatus(NotificationStatus.Sending);
        Status = NotificationStatus.Sent;
        SentAt = now;
        ProviderMessageId = providerMessageId;
        LockedUntil = null;
        LastError = null;
    }

    /// <summary>Lỗi tạm: còn lượt ⇒ hẹn giờ thử lại (<paramref name="retryAfter"/> của nhà cung cấp nếu có); hết lượt ⇒ <c>Failed</c>.</summary>
    public void ScheduleRetry(DateTimeOffset now, string error, TimeSpan? retryAfter)
    {
        EnsureStatus(NotificationStatus.Sending);
        LastError = error;
        LockedUntil = null;
        if (RetryPolicy.NextDelay(Attempts) is not { } delay)
        {
            Status = NotificationStatus.Failed;
            return;
        }
        Status = NotificationStatus.Pending;
        NextAttemptAt = now + (retryAfter ?? delay);
    }

    /// <summary>Lỗi vĩnh viễn: không thử lại.</summary>
    public void MarkFailed(string error)
    {
        EnsureStatus(NotificationStatus.Sending);
        Status = NotificationStatus.Failed;
        LastError = error;
        LockedUntil = null;
    }

    /// <summary>NT-BR-07: nhắc nợ nhưng phiếu đã hết nợ / đã hủy.</summary>
    public void MarkSkipped()
    {
        EnsureStatus(NotificationStatus.Sending);
        Status = NotificationStatus.Skipped;
        LockedUntil = null;
    }

    /// <summary>Hủy phiếu / hủy liên kết Zalo. Chỉ tin đang chờ; tin đang gửi không thu hồi được.</summary>
    public bool Cancel()
    {
        if (Status != NotificationStatus.Pending)
            return false;
        Status = NotificationStatus.Cancelled;
        return true;
    }

    /// <summary>NT-BR-16.</summary>
    public void ClearRecipient() => Recipient = null;

    private void EnsureStatus(NotificationStatus expected)
    {
        if (Status != expected)
            throw new InvalidOperationException($"Notification is {Status}; expected {expected}.");
    }
}
