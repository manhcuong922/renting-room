using renting_room.Domain.Common;

namespace renting_room.Domain.Notifications;

public static class NotificationErrors
{
    public static readonly Error InvalidReminderDays = Error.Validation("INVALID_REMINDER_DAYS",
        "Ngày nhắc nợ: tối đa 3 mốc, mỗi mốc 1–60 ngày sau hạn, không trùng và tăng dần.");

    public static readonly Error InvalidChannel = Error.Validation("INVALID_CHANNEL", "Kênh chỉ là Zalo hoặc Email, không trùng nhau.");

    public static readonly Error InvalidBillingMonth = Error.Validation("INVALID_BILLING_MONTH", "Tháng thu phải là ngày đầu tháng (yyyy-MM-01).");

    public static readonly Error ZaloNotConfigured = Error.Conflict("ZALO_BOT_NOT_CONFIGURED",
        "Máy chủ chưa cấu hình Zalo Bot — liên hệ người vận hành đặt ZaloBot:Token.");

    public static readonly Error InvoiceNotFinalized = Error.Conflict("INVOICE_NOT_FINALIZED", "Phiếu chưa chốt — chỉ gửi được phiếu đã chốt.");

    public static readonly Error InvoiceVoided = Error.Conflict("INVOICE_VOIDED", "Phiếu đã hủy — không gửi.");

    public static readonly Error NoChannel = Error.BusinessRule("NO_NOTIFICATION_CHANNEL",
        "Người đứng tên chưa có kênh nhận tin (chưa liên kết Zalo, chưa có email, hoặc kênh chưa cấu hình ở máy chủ). Dùng nội dung chia sẻ tay.");
}
