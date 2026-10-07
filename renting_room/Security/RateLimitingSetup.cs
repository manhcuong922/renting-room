using System.ComponentModel.DataAnnotations;
using System.Net;
using System.Net.Sockets;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using renting_room.Application.Common.Security;
using renting_room.Errors;

namespace renting_room.Security;

/// <summary>Một hạn mức: tối đa <see cref="PermitLimit"/> request trong <see cref="WindowSeconds"/> giây (cửa sổ trượt).</summary>
public sealed class RateLimitRule
{
    [Range(1, 1_000_000)]
    public int PermitLimit { get; init; }

    [Range(1, 3600)]
    public int WindowSeconds { get; init; } = 60;
}

public sealed class RateLimitingOptions
{
    public const string SectionName = "RateLimiting";

    /// <summary>Tắt toàn bộ (chỉ dùng khi debug cục bộ).</summary>
    public bool Enabled { get; init; } = true;

    /// <summary>Đăng nhập — theo IP, mọi tài khoản cộng lại (M01 §11: 20/phút).</summary>
    [Required] public RateLimitRule Login { get; init; } = new() { PermitLimit = 20 };

    /// <summary>
    /// Đăng nhập — theo IP + tài khoản (M01 §11: 5/phút): chặn dò mật khẩu một tài khoản và giảm việc cố ý gõ sai
    /// để khóa tài khoản người khác (ID-BR-06), trong khi người khác cùng IP (cùng wifi nhà trọ) vẫn đăng nhập được.
    /// </summary>
    [Required] public RateLimitRule LoginPerAccount { get; init; } = new() { PermitLimit = 5 };

    /// <summary>Làm mới token / đăng xuất — theo IP.</summary>
    [Required] public RateLimitRule Refresh { get; init; } = new() { PermitLimit = 30 };

    /// <summary>Thao tác nhạy cảm (đổi mật khẩu, tạo tổ chức…) — theo user.</summary>
    [Required] public RateLimitRule Sensitive { get; init; } = new() { PermitLimit = 5 };

    /// <summary>Mọi request của user đã đăng nhập.</summary>
    [Required] public RateLimitRule Authenticated { get; init; } = new() { PermitLimit = 300 };

    /// <summary>Mọi request chưa đăng nhập — theo IP.</summary>
    [Required] public RateLimitRule Anonymous { get; init; } = new() { PermitLimit = 60 };

    /// <summary>Request ghi (POST/PUT/PATCH/DELETE) — chặt hơn đọc, chống spam tạo dữ liệu.</summary>
    [Required] public RateLimitRule Write { get; init; } = new() { PermitLimit = 60 };

    /// <summary>Số request đang xử lý ĐỒNG THỜI tối đa của một client — chống bắn song song hàng loạt.</summary>
    [Range(1, 1000)]
    public int MaxConcurrentRequestsPerClient { get; init; } = 10;
}

public static class RateLimitPolicies
{
    public const string Login = "login";
    public const string Refresh = "refresh";
    public const string Sensitive = "sensitive";
}

/// <summary>
/// Chống spam nhiều lớp. Mọi request đi qua GlobalLimiter (chuỗi 3 limiter); endpoint đặc biệt thêm policy riêng:
/// <code>
/// Global (mọi request, trừ /health):
///   1. Theo client: user đã đăng nhập → Authenticated; chưa đăng nhập → Anonymous (theo IP)
///   2. Request ghi (POST/PUT/PATCH/DELETE) → Write
///   3. Số request song song → MaxConcurrentRequestsPerClient
/// Policy theo endpoint: login, refresh (theo IP) · sensitive (theo user)
/// Trong endpoint login: theo IP + tài khoản (<see cref="LoginAttemptLimiter"/> — cần username trong body)
/// </code>
/// Dùng cửa sổ TRƯỢT (6 phân đoạn) thay cho cửa sổ cố định: cửa sổ cố định cho phép bắn gấp đôi giới hạn
/// ở ranh giới (cuối phút trước + đầu phút sau).
/// </summary>
internal static class RateLimitingSetup
{
    private const int SegmentsPerWindow = 6;

    public static IServiceCollection AddApiRateLimiting(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<RateLimitingOptions>()
            .Bind(configuration.GetSection(RateLimitingOptions.SectionName))
            .ValidateDataAnnotations()
            .Validate(o => new[] { o.Login, o.LoginPerAccount, o.Refresh, o.Sensitive, o.Authenticated, o.Anonymous, o.Write }
                    .All(r => r.PermitLimit >= 1 && r.WindowSeconds is >= 1 and <= 3600),
                "Every RateLimiting rule needs PermitLimit >= 1 and WindowSeconds in [1, 3600].")
            .ValidateOnStart();
        services.AddSingleton<LoginAttemptLimiter>();

        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.OnRejected = OnRejectedAsync;

            options.AddPolicy(RateLimitPolicies.Login, http =>
                Sliding($"login:{ClientIp(http)}", Settings(http), s => s.Login));
            options.AddPolicy(RateLimitPolicies.Refresh, http =>
                Sliding($"refresh:{ClientIp(http)}", Settings(http), s => s.Refresh));
            options.AddPolicy(RateLimitPolicies.Sensitive, http =>
                Sliding($"sensitive:{ClientKey(http)}", Settings(http), s => s.Sensitive));

            options.GlobalLimiter = PartitionedRateLimiter.CreateChained(
                PartitionedRateLimiter.Create<HttpContext, string>(http =>
                {
                    var settings = Settings(http);
                    if (IsExempt(http, settings))
                        return RateLimitPartition.GetNoLimiter("exempt");

                    return IsAuthenticated(http)
                        ? Sliding($"all:{ClientKey(http)}", settings, s => s.Authenticated)
                        : Sliding($"all:{ClientKey(http)}", settings, s => s.Anonymous);
                }),
                PartitionedRateLimiter.Create<HttpContext, string>(http =>
                {
                    var settings = Settings(http);
                    if (IsExempt(http, settings) || IsReadOnly(http.Request.Method))
                        return RateLimitPartition.GetNoLimiter("read");

                    return Sliding($"write:{ClientKey(http)}", settings, s => s.Write);
                }),
                PartitionedRateLimiter.Create<HttpContext, string>(http =>
                {
                    var settings = Settings(http);
                    if (IsExempt(http, settings))
                        return RateLimitPartition.GetNoLimiter("exempt");

                    return RateLimitPartition.GetConcurrencyLimiter($"concurrent:{ClientKey(http)}", _ =>
                        new ConcurrencyLimiterOptions
                        {
                            PermitLimit = settings.MaxConcurrentRequestsPerClient,
                            QueueLimit = 0
                        });
                }));
        });

        return services;
    }

    private static async ValueTask OnRejectedAsync(OnRejectedContext context, CancellationToken cancellationToken)
    {
        var retryAfter = PrepareRejection(context.HttpContext, context.Lease);
        await ProblemResponses.WriteAsync(context.HttpContext, StatusCodes.Status429TooManyRequests, TooManyRequestsCode,
            TooManyRequestsMessage(retryAfter));
    }

    /// <summary>429 cho limiter gọi trong endpoint (ngoài middleware) — cùng header, log và nội dung với <see cref="OnRejectedAsync"/>.</summary>
    internal static ProblemHttpResult TooManyRequests(HttpContext http, RateLimitLease lease) =>
        ProblemResponses.Problem(StatusCodes.Status429TooManyRequests, TooManyRequestsCode,
            TooManyRequestsMessage(PrepareRejection(http, lease)));

    private const string TooManyRequestsCode = "TOO_MANY_REQUESTS";

    private static string TooManyRequestsMessage(int retryAfterSeconds) =>
        $"Bạn thao tác quá nhanh. Vui lòng thử lại sau {retryAfterSeconds} giây.";

    /// <summary>Đặt header Retry-After + ghi log; trả số giây chờ.</summary>
    private static int PrepareRejection(HttpContext http, RateLimitLease lease)
    {
        // Limiter đồng thời không có RetryAfter — mặc định 1 giây.
        var retryAfter = lease.TryGetMetadata(MetadataName.RetryAfter, out var value)
            ? Math.Max(1, (int)Math.Ceiling(value.TotalSeconds))
            : 1;
        http.Response.Headers.RetryAfter = retryAfter.ToString();

        http.RequestServices.GetRequiredService<ILoggerFactory>()
            .CreateLogger(nameof(RateLimitingSetup))
            .LogWarning("Rate limit exceeded for {ClientKey} on {Method} {Path}", ClientKey(http), http.Request.Method, http.Request.Path);
        return retryAfter;
    }

    private static RateLimitingOptions Settings(HttpContext http) =>
        http.RequestServices.GetRequiredService<IOptions<RateLimitingOptions>>().Value;

    private static bool IsExempt(HttpContext http, RateLimitingOptions settings) =>
        !settings.Enabled || http.Request.Path.StartsWithSegments("/health");

    private static bool IsAuthenticated(HttpContext http) => http.User.Identity?.IsAuthenticated == true;

    private static bool IsReadOnly(string method) =>
        HttpMethods.IsGet(method) || HttpMethods.IsHead(method) || HttpMethods.IsOptions(method);

    /// <summary>User đã đăng nhập → theo user (đổi IP không lách được); chưa đăng nhập → theo IP.</summary>
    private static string ClientKey(HttpContext http)
    {
        var userId = IsAuthenticated(http) ? http.User.FindFirst(AppClaimTypes.Subject)?.Value : null;
        return userId is not null ? $"user:{userId}" : $"ip:{ClientIp(http)}";
    }

    /// <summary>
    /// IPv6: gom theo dải /64 — một khách hàng thường được cấp cả dải /64,
    /// nếu khóa theo từng địa chỉ thì chỉ cần đổi địa chỉ là có "bucket" mới và vượt giới hạn.
    /// </summary>
    internal static string ClientIp(HttpContext http)
    {
        var address = http.Connection.RemoteIpAddress;
        if (address is null)
            return "unknown";

        if (address.IsIPv4MappedToIPv6)
            address = address.MapToIPv4();

        if (address.AddressFamily != AddressFamily.InterNetworkV6)
            return address.ToString();

        var prefix = address.GetAddressBytes()[..8].Concat(new byte[8]).ToArray();
        return $"{new IPAddress(prefix)}/64";
    }

    private static RateLimitPartition<string> Sliding(
        string key, RateLimitingOptions settings, Func<RateLimitingOptions, RateLimitRule> selectRule)
    {
        if (!settings.Enabled)
            return RateLimitPartition.GetNoLimiter(key);

        var rule = selectRule(settings);
        return RateLimitPartition.GetSlidingWindowLimiter(key, _ => SlidingOptions(rule));
    }

    internal static SlidingWindowRateLimiterOptions SlidingOptions(RateLimitRule rule) => new()
    {
        PermitLimit = rule.PermitLimit,
        Window = TimeSpan.FromSeconds(rule.WindowSeconds),
        SegmentsPerWindow = SegmentsPerWindow,
        QueueLimit = 0,
        AutoReplenishment = true
    };
}
