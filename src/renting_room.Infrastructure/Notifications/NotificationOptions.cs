using System.ComponentModel.DataAnnotations;
using System.Text.RegularExpressions;

namespace renting_room.Infrastructure.Notifications;

/// <summary>
/// Cấu hình Zalo Bot (<c>ZaloBot</c>). Để trống <see cref="Token"/> ⇒ kênh Zalo "chưa cấu hình": app vẫn khởi động, poller không chạy, tạo mã liên kết trả
/// <c>ZALO_BOT_NOT_CONFIGURED</c>. <see cref="Token"/> là mật khẩu của bot — đặt qua user-secrets / biến môi trường (<c>ZaloBot__Token</c>);
/// KHÔNG commit token vào <c>appsettings*.json</c> (cả hai file đang được git theo dõi) và không ghi log (M11 §11).
/// </summary>
public sealed partial class ZaloBotOptions
{
    public const string SectionName = "ZaloBot";

    /// <summary>Dạng <c>1234567890:abcDEF…</c> do Zalo cấp khi tạo bot.</summary>
    public string? Token { get; init; }

    /// <summary>Địa chỉ gốc Zalo Bot API (chưa chạy thử — NT-T00; sửa ở đây nếu Zalo đổi).</summary>
    [Url]
    public string ApiBaseUrl { get; init; } = "https://bot-api.zaloplatforms.com";

    /// <summary>Tên bot hiển thị cho chủ trọ khi tạo mã liên kết. Trống ⇒ hỏi <c>getMe</c> lần đầu rồi nhớ lại.</summary>
    public string? BotName { get; init; }

    /// <summary>Tắt để chạy nhiều instance mà chỉ một instance nhận tin (long-polling không chia việc).</summary>
    public bool PollerEnabled { get; init; } = true;

    [Range(1, 60)]
    public int PollTimeoutSeconds { get; init; } = 25;

    public bool IsConfigured => !string.IsNullOrWhiteSpace(Token);

    public static bool IsValidToken(string? token) => string.IsNullOrWhiteSpace(token) || TokenPattern().IsMatch(token);

    [GeneratedRegex(@"^\d+:\S+$")]
    private static partial Regex TokenPattern();
}

/// <summary>Dịch vụ nền gửi tin (<c>Notifications</c>): hàng đợi outbox, khung giờ, tốc độ, nhắc nợ, dọn dẹp.</summary>
public sealed class NotificationOptions
{
    public const string SectionName = "Notifications";

    /// <summary>Tắt vòng nhận việc (test tự gọi <c>RunOnceAsync</c>; chạy nhiều instance thì chỉ bật một).</summary>
    public bool DispatcherEnabled { get; init; } = true;

    [Range(1, 60)]
    public int DispatchIntervalSeconds { get; init; } = 2;

    [Range(1, 200)]
    public int BatchSize { get; init; } = 20;

    [Range(1, 60)]
    public int LeaseMinutes { get; init; } = 5;

    /// <summary>NT-BR-08: khung giờ VN được gửi tin tự động [Start, End). <c>ManualResend</c> không bị giới hạn.</summary>
    [Range(0, 23)]
    public int SendWindowStartHour { get; init; } = 8;

    [Range(1, 24)]
    public int SendWindowEndHour { get; init; } = 20;

    /// <summary>NT-BR-11: token bucket mỗi kênh.</summary>
    [Range(0.1, 1000)]
    public double ZaloPerSecond { get; init; } = 5;

    [Range(0.1, 1000)]
    public double EmailPerSecond { get; init; } = 1;

    public bool ReminderSchedulerEnabled { get; init; } = true;

    [Range(1, 1440)]
    public int ReminderIntervalMinutes { get; init; } = 60;

    /// <summary>NT-BR-18.</summary>
    [Range(30, 3650)]
    public int RetentionDays { get; init; } = 180;
}
