using System.Threading.RateLimiting;
using Microsoft.Extensions.Options;

namespace renting_room.Security;

/// <summary>
/// M-13: hạn mức dò số giấy tờ theo user (<see cref="RateLimitingOptions.IdLookup"/>). Chỉ dùng được trong endpoint sau khi đọc body
/// (policy của middleware chạy trước model binding, không biết request có mang số giấy tờ hay không) — giống <see cref="LoginAttemptLimiter"/>.
/// </summary>
public sealed class IdNumberLookupLimiter : IDisposable
{
    private readonly PartitionedRateLimiter<string> _limiter;

    public IdNumberLookupLimiter(IOptions<RateLimitingOptions> options)
    {
        var settings = options.Value;
        _limiter = PartitionedRateLimiter.Create<string, string>(key => settings.Enabled
            ? RateLimitPartition.GetSlidingWindowLimiter(key, _ => RateLimitingSetup.SlidingOptions(settings.IdLookup))
            : RateLimitPartition.GetNoLimiter(key));
    }

    /// <summary>Lease phải được dispose; <see cref="RateLimitLease.IsAcquired"/> = false ⇒ trả 429.</summary>
    public RateLimitLease Acquire(HttpContext http) => _limiter.AttemptAcquire($"idlookup:{RateLimitingSetup.ClientKey(http)}");

    public void Dispose() => _limiter.Dispose();
}
