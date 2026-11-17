using renting_room.Domain.Notifications;

namespace renting_room.Application.Notifications;

/// <summary>Tin đã dựng xong: <paramref name="Subject"/> chỉ dùng cho email (Zalo bỏ qua).</summary>
public sealed record NotificationMessage(string Recipient, string Subject, string Text);

public enum SendOutcome
{
    Ok,

    /// <summary>Timeout, 5xx, 429 — thử lại theo <see cref="RetryPolicy"/> (NT-BR-09).</summary>
    Transient,

    /// <summary>Lỗi vĩnh viễn (sai cấu hình, nội dung bị từ chối) — không thử lại (NT-BR-10).</summary>
    Permanent,

    /// <summary>Người nhận chặn bot / chat không tồn tại — thất bại và xóa liên kết Zalo (NT-BR-10).</summary>
    RecipientUnreachable
}

/// <param name="ErrorCode">Mã ngắn lưu vào <c>last_error</c>. KHÔNG chứa token / nội dung tin.</param>
/// <param name="RetryAfter">429 kèm <c>retry_after</c> ⇒ dùng đúng giá trị đó (NT-BR-09).</param>
public sealed record SendResult(SendOutcome Outcome, string? ProviderMessageId = null, string? ErrorCode = null, TimeSpan? RetryAfter = null)
{
    public static SendResult Ok(string? providerMessageId) => new(SendOutcome.Ok, providerMessageId);
    public static SendResult Transient(string code, TimeSpan? retryAfter = null) => new(SendOutcome.Transient, null, code, retryAfter);
    public static SendResult Permanent(string code) => new(SendOutcome.Permanent, null, code);
    public static SendResult Unreachable(string code) => new(SendOutcome.RecipientUnreachable, null, code);
}

/// <summary>Mỗi kênh một bản (Zalo, Email). Không ném lỗi — mọi sự cố thành <see cref="SendResult"/>.</summary>
public interface INotificationSender
{
    NotificationChannel Channel { get; }

    /// <summary>Máy chủ đã cấu hình kênh này (token Zalo / SMTP). Chưa cấu hình ⇒ không tạo tin cho kênh (NT-BR-03).</summary>
    bool IsConfigured { get; }

    Task<SendResult> SendAsync(NotificationMessage message, CancellationToken cancellationToken);
}

/// <summary>Thông tin kênh cho UI / API (NT-UC-11) và để bỏ qua kênh chưa cấu hình khi tạo tin.</summary>
public interface INotificationChannels
{
    bool ZaloConfigured { get; }
    bool EmailConfigured { get; }

    /// <summary>Tên bot để chủ trọ biết người thuê nhắn cho ai; null nếu chưa cấu hình / chưa lấy được.</summary>
    Task<string?> GetZaloBotNameAsync(CancellationToken cancellationToken);

    bool IsConfigured(NotificationChannel channel) => channel == NotificationChannel.Zalo ? ZaloConfigured : EmailConfigured;
}

/// <summary>Băm mã liên kết bằng HMAC có khóa (L-19) — lộ DB không dựng lại được mã.</summary>
public interface ILinkCodeHasher
{
    string Hash(string normalizedCode);
}

/// <param name="UpdateId">Dùng làm mốc <c>offset</c> cho lần gọi sau.</param>
public sealed record ZaloUpdate(long? UpdateId, string ChatId, string? Text);

public sealed record ZaloUpdatesResult(bool Ok, IReadOnlyList<ZaloUpdate> Updates, string? ErrorCode = null, TimeSpan? RetryAfter = null);

/// <summary>Cổng gọi Zalo Bot Platform (M11 §6). Hiện thực: <c>ZaloBotClient</c> (HttpClient); test dùng bản giả.</summary>
public interface IZaloBotClient
{
    bool IsConfigured { get; }

    Task<string?> GetBotNameAsync(CancellationToken cancellationToken);

    Task<ZaloUpdatesResult> GetUpdatesAsync(long? offset, int timeoutSeconds, CancellationToken cancellationToken);

    Task<SendResult> SendMessageAsync(string chatId, string text, CancellationToken cancellationToken);
}
