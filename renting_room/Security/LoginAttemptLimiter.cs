using System.Threading.RateLimiting;
using Microsoft.Extensions.Options;
using renting_room.Domain.Identity;

namespace renting_room.Security;

/// <summary>
/// Giới hạn đăng nhập theo IP + tài khoản (<see cref="RateLimitingOptions.LoginPerAccount"/>, M01 §11).
/// Không làm được bằng policy của RateLimiter middleware: khóa cần username trong body mà partitioner chạy trước model binding
/// ⇒ endpoint login gọi trực tiếp sau khi đã đọc body. Giới hạn theo IP vẫn do policy <c>login</c> đảm nhận.
/// </summary>
internal sealed class LoginAttemptLimiter : IDisposable
{
    private readonly PartitionedRateLimiter<string> _limiter;

    public LoginAttemptLimiter(IOptions<RateLimitingOptions> options)
    {
        var settings = options.Value;
        _limiter = PartitionedRateLimiter.Create<string, string>(key => settings.Enabled
            ? RateLimitPartition.GetSlidingWindowLimiter(key, _ => RateLimitingSetup.SlidingOptions(settings.LoginPerAccount))
            : RateLimitPartition.GetNoLimiter(key));
    }

    /// <summary>Lease phải được dispose; <see cref="RateLimitLease.IsAcquired"/> = false ⇒ trả 429.</summary>
    public RateLimitLease Acquire(HttpContext http, string? username) =>
        _limiter.AttemptAcquire($"{RateLimitingSetup.ClientIp(http)}|{AccountKey(username)}");

    /// <summary>"0912…", "+84 912…", "84912…" là cùng một tài khoản — không lách được bằng cách đổi cách viết.</summary>
    private static string AccountKey(string? username) =>
        ContactNormalizer.NormalizeEmail(username)
        ?? ContactNormalizer.NormalizePhone(username)
        ?? username?.Trim().ToLowerInvariant()
        ?? string.Empty;

    public void Dispose() => _limiter.Dispose();
}
