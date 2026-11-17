namespace renting_room.Domain.Notifications;

/// <summary>M11 §2: kênh gửi tin tới người thuê.</summary>
public enum NotificationChannel
{
    Zalo,
    Email
}

/// <summary>M11 §2: <c>InvoiceIssued</c> phiếu mới chốt · <c>OverdueReminder</c> nhắc nợ · <c>ManualResend</c> chủ trọ bấm gửi lại.</summary>
public enum NotificationKind
{
    InvoiceIssued,
    OverdueReminder,
    ManualResend
}

/// <summary>M11 §3.3. <c>Sent</c>, <c>Failed</c>, <c>Skipped</c>, <c>Cancelled</c> là trạng thái kết thúc.</summary>
public enum NotificationStatus
{
    Pending,
    Sending,
    Sent,
    Failed,
    Skipped,
    Cancelled
}
