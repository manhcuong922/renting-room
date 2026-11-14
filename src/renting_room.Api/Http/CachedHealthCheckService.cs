using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace renting_room.Http;

internal static class CachedHealthChecksExtensions
{
    /// <summary>
    /// M-16: <c>/health/ready</c> ẩn danh và miễn rate limit, mỗi lần gọi mở một kết nối DB ⇒ kẻ lạ gọi dồn dập làm cạn pool.
    /// Bọc <see cref="HealthCheckService"/> mặc định: kết quả kiểm tra có chạy check được dùng lại trong <paramref name="ttl"/>.
    /// </summary>
    public static IServiceCollection AddCachedHealthChecks(this IServiceCollection services, TimeSpan ttl)
    {
        var descriptor = services.Last(d => d.ServiceType == typeof(HealthCheckService));
        var implementation = descriptor.ImplementationType
            ?? throw new InvalidOperationException("HealthCheckService is expected to be registered by AddHealthChecks().");
        services.Remove(descriptor);
        services.AddSingleton<HealthCheckService>(sp => new CachedHealthCheckService(
            (HealthCheckService)ActivatorUtilities.CreateInstance(sp, implementation), sp.GetRequiredService<TimeProvider>(), ttl));
        return services;
    }
}

internal sealed class CachedHealthCheckService(HealthCheckService inner, TimeProvider clock, TimeSpan ttl) : HealthCheckService
{
    private readonly Lock _gate = new();
    private readonly Dictionary<Func<HealthCheckRegistration, bool>, (DateTimeOffset At, HealthReport Report)> _cache = [];
    private static readonly Func<HealthCheckRegistration, bool> AllChecks = _ => true;

    public override async Task<HealthReport> CheckHealthAsync(
        Func<HealthCheckRegistration, bool>? predicate, CancellationToken cancellationToken = default)
    {
        var key = predicate ?? AllChecks;
        var now = clock.GetUtcNow();
        lock (_gate)
        {
            if (_cache.TryGetValue(key, out var hit) && now - hit.At < ttl)
                return hit.Report;
        }

        var report = await inner.CheckHealthAsync(predicate, cancellationToken);
        // Predicate không chạy check nào (liveness) rẻ sẵn — không cần giữ cache.
        if (report.Entries.Count > 0)
            lock (_gate)
                _cache[key] = (now, report);
        return report;
    }
}
