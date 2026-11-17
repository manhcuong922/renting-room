namespace renting_room.Domain.Notifications;

/// <summary>NT-BR-09: lỗi tạm ⇒ thử lại sau 1 → 5 → 30 → 120 phút; quá 5 lượt ⇒ thất bại.</summary>
public static class RetryPolicy
{
    public const int MaxAttempts = 5;

    private static readonly TimeSpan[] Delays = [TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(30), TimeSpan.FromMinutes(120)];

    /// <param name="attempts">Số lượt đã gửi (kể cả lượt vừa lỗi).</param>
    /// <returns>Chờ bao lâu rồi thử lại; null = hết lượt.</returns>
    public static TimeSpan? NextDelay(int attempts) => attempts is >= 1 and < MaxAttempts ? Delays[attempts - 1] : null;
}
