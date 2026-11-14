using renting_room.Domain.Common;

namespace renting_room.Application.Common.Interfaces;

/// <summary>Thư văn bản thuần (text/plain) — không HTML ⇒ không có chèn mã vào nội dung (M11 NT-BR-17).</summary>
public sealed record EmailMessage(string To, string Subject, string Body);

/// <summary>
/// Gửi email hệ thống. Bản đầu của kênh email M11 (NT-T06): hiện chỉ dùng để gửi mật khẩu tạm tới email của chính chủ (M-14); M11 dùng lại
/// cho phiếu báo tiền phòng.
/// </summary>
public interface IEmailSender
{
    /// <summary>Máy chủ đã cấu hình SMTP (<c>Email:Smtp:Host</c> + <c>FromAddress</c>).</summary>
    bool IsConfigured { get; }

    /// <summary>Thất bại: <see cref="EmailErrors.NotConfigured"/> hoặc <see cref="EmailErrors.SendFailed"/> (không ném lỗi, không ghi nội dung thư vào log).</summary>
    Task<Result> SendAsync(EmailMessage message, CancellationToken cancellationToken);
}

public static class EmailErrors
{
    public static readonly Error NotConfigured = Error.Unavailable("EMAIL_NOT_CONFIGURED",
        "Máy chủ chưa cấu hình gửi email — liên hệ người vận hành để cấu hình SMTP (Email:Smtp).");

    public static readonly Error SendFailed = Error.Unavailable("EMAIL_SEND_FAILED",
        "Không gửi được email — máy chủ thư từ chối hoặc không phản hồi. Chưa có thay đổi nào, thử lại sau.");
}
