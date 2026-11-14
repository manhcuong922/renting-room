using System.Net;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.OpenApi;
using renting_room.Application.Common.Interfaces;
using renting_room.Errors;
using renting_room.Http;
using renting_room.Idempotency;
using renting_room.Infrastructure.Identity;
using renting_room.Infrastructure.Jobs;
using renting_room.Infrastructure.Persistence;
using renting_room.Middleware;
using renting_room.OpenApi;
using renting_room.Security;

namespace renting_room;

internal static class ApiSetup
{
    private const string CorsPolicy = "frontend";
    private const string ReadyTag = "ready";

    public static IServiceCollection AddApi(this IServiceCollection services, IConfiguration configuration)
    {
        services.ConfigureHttpJsonOptions(options =>
        {
            options.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
            options.SerializerOptions.Converters.Add(new NormalizingStringConverter());
            // "version" trả ra dạng chuỗi (xmin) — client gửi lại nguyên chuỗi đó khi cập nhật.
            options.SerializerOptions.NumberHandling = JsonNumberHandling.AllowReadingFromString;
        });
        // Đăng ký TRƯỚC controller: bộ ghi ProblemDetails mặc định được chọn trước bộ ghi của MVC ⇒ mọi lỗi cùng một định dạng.
        services.AddApiProblemDetails();
        services.AddExceptionHandler<GlobalExceptionHandler>();

        // Controller đọc body bằng JsonOptions của MVC — cấu hình giống hệt phần trên. Swagger cũng mô tả enum theo cấu hình này.
        services.AddControllers(options =>
            {
                options.SuppressImplicitRequiredAttributeForNonNullableReferenceTypes = true;
                options.ModelMetadataDetailsProviders.Add(new RequiredParameterMetadataProvider());
            })
            .AddJsonOptions(options =>
            {
                options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
                options.JsonSerializerOptions.Converters.Add(new NormalizingStringConverter());
                options.JsonSerializerOptions.NumberHandling = JsonNumberHandling.AllowReadingFromString;
            })
            // JSON sai cú pháp / thiếu body / tham số sai kiểu hoặc thiếu → BadHttpRequestException ⇒ GlobalExceptionHandler trả
            // 400 INVALID_REQUEST. Kiểm tra nghiệp vụ do FluentValidation làm.
            .ConfigureApiBehaviorOptions(options => options.InvalidModelStateResponseFactory = _ =>
                throw new BadHttpRequestException(ProblemResponses.InvalidRequestDetail));

        services.AddHttpContextAccessor();
        // Job nền đặt người dùng hệ thống của tổ chức qua CurrentUserOverride; còn lại là người dùng của request.
        services.AddScoped<HttpContextCurrentUser>();
        services.AddScoped<ICurrentUser>(sp =>
            sp.GetRequiredService<CurrentUserOverride>().User ?? sp.GetRequiredService<HttpContextCurrentUser>());
        services.AddJwtAuthentication();
        services.AddAppAuthorization();
        services.AddApiRateLimiting(configuration);

        // Mã hóa dữ liệu nhạy cảm lưu tạm (response idempotency chứa token / mật khẩu tạm).
        // Nhiều instance / container: PHẢI cấu hình DataProtection:KeysPath trỏ tới thư mục dùng chung và bền vững,
        // nếu không mỗi instance có khóa riêng và mất khóa khi container khởi động lại. Ngoài Development thiếu ⇒ app dừng (M-17, UseApi).
        var dataProtection = services.AddDataProtection().SetApplicationName("renting_room");
        if (configuration["DataProtection:KeysPath"] is { Length: > 0 } keysPath)
            dataProtection.PersistKeysToFileSystem(new DirectoryInfo(keysPath));

        services.AddCors(options =>
        {
            var origins = configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
            options.AddPolicy(CorsPolicy, policy => policy
                .WithOrigins(origins)
                .AllowAnyHeader()
                .AllowAnyMethod()
                .WithExposedHeaders("Location", "Retry-After", IdempotencyEndpointExtensions.ReplayedHeaderName));
        });

        // Sau reverse proxy (nginx, load balancer): chỉ tin X-Forwarded-For/Proto từ proxy đã khai báo.
        // KHÔNG BAO GIỜ xóa trắng KnownProxies/KnownIPNetworks — khi đó client tự giả IP được và vượt rate limit.
        services.Configure<ForwardedHeadersOptions>(options =>
        {
            options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
            foreach (var proxy in configuration.GetSection("ReverseProxy:KnownProxies").Get<string[]>() ?? [])
                options.KnownProxies.Add(IPAddress.Parse(proxy));
            foreach (var network in configuration.GetSection("ReverseProxy:KnownNetworks").Get<string[]>() ?? [])
                options.KnownIPNetworks.Add(ParseNetwork(network));
        });

        services.AddHealthChecks().AddDbContextCheck<AppDbContext>("database", tags: [ReadyTag]);
        services.AddCachedHealthChecks(TimeSpan.FromSeconds(configuration.GetValue("Health:ReadyCacheSeconds", 5)));

        services.AddEndpointsApiExplorer();
        services.AddSwaggerGen(options =>
        {
            options.SwaggerDoc("v1", new OpenApiInfo { Title = "Renting Room API", Version = "v1" });
            options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
            {
                Name = "Authorization",
                Type = SecuritySchemeType.Http,
                Scheme = "bearer",
                BearerFormat = "JWT",
                In = ParameterLocation.Header,
                Description = "Dán access token (không cần gõ chữ 'Bearer')."
            });
            options.AddSecurityRequirement(document => new OpenApiSecurityRequirement
            {
                [new OpenApiSecuritySchemeReference("Bearer", document)] = []
            });
            options.OperationFilter<IdempotencyHeaderOperationFilter>();
            options.ParameterFilter<RequiredFromMetadataFilter>();
            options.RequestBodyFilter<RequiredFromMetadataFilter>();
        });

        return services;
    }

    public static WebApplication UseApi(this WebApplication app)
    {
        // Fail-fast: thiếu/sai Jwt:SigningKey ở production phải làm app dừng ngay khi khởi động,
        // không phải đợi tới request đăng nhập đầu tiên mới trả 500.
        _ = app.Services.GetRequiredService<JwtSigningKeyProvider>();
        _ = app.Services.GetRequiredService<IPersonalDataProtector>(); // thiếu PersonalData:HashKey ⇒ dừng ngay
        RequireAllowedHosts(app);
        RequireDataProtectionKeysPath(app);
        WarnIfReverseProxyNotConfigured(app);

        app.UseForwardedHeaders();
        app.UseExceptionHandler();
        app.UseStatusCodePages(); // 404/405 không có body → ProblemDetails

        if (!app.Environment.IsDevelopment())
            app.UseHsts();

        app.UseHttpsRedirection();
        app.UseMiddleware<SecurityHeadersMiddleware>();

        if (app.Configuration.GetValue<bool>("Swagger:Enabled"))
        {
            app.UseSwagger();
            app.UseSwaggerUI();
        }

        app.UseCors(CorsPolicy);
        app.UseAuthentication();
        app.UseRateLimiter(); // sau Authentication để giới hạn theo user
        app.UseAuthorization();
        app.UseMiddleware<IdempotencyMiddleware>(); // sau Authorization: request bị từ chối quyền không chiếm key

        app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false }).AllowAnonymous();
        app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = c => c.Tags.Contains(ReadyTag) })
            .AllowAnonymous();

        return app;
    }

    private static System.Net.IPNetwork ParseNetwork(string cidr) =>
        System.Net.IPNetwork.TryParse(cidr, out var network)
            ? network
            : throw new InvalidOperationException($"ReverseProxy:KnownNetworks entry '{cidr}' is not a valid CIDR.");

    /// <summary>
    /// Sau proxy mà không khai báo proxy ⇒ mọi client có chung IP của proxy ⇒ giới hạn đăng nhập theo IP
    /// áp cho TẤT CẢ người dùng cùng lúc. Cảnh báo để người vận hành cấu hình đúng.
    /// </summary>
    /// <summary>
    /// L-15 (05/11/2026, M12 §5): ngoài Development, <c>AllowedHosts</c> phải là tên miền thật (VD <c>tro.example.vn</c>, nhiều tên cách nhau
    /// bằng <c>;</c>) — <c>"*"</c> nhận mọi Host header ⇒ dừng ngay khi khởi động thay vì chạy với cấu hình lỏng.
    /// </summary>
    private static void RequireAllowedHosts(WebApplication app)
    {
        if (app.Environment.IsDevelopment())
            return;
        var hosts = (app.Configuration["AllowedHosts"] ?? string.Empty)
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (hosts.Length == 0 || hosts.Contains("*"))
            throw new InvalidOperationException(
                "AllowedHosts must list the public host names (e.g. \"tro.example.vn\") outside Development; \"*\" accepts any Host header. " +
                "Set AllowedHosts via environment variable.");
    }

    /// <summary>
    /// M-17: khóa Data Protection mất khi container chạy lại ⇒ mọi <c>id_number_encrypted</c> và response idempotency thành rác không giải mã được.
    /// Ngoài Development bắt buộc trỏ <c>DataProtection:KeysPath</c> tới volume bền vững, thiếu thì dừng ngay khi khởi động.
    /// </summary>
    private static void RequireDataProtectionKeysPath(WebApplication app)
    {
        if (app.Environment.IsDevelopment())
            return;
        var keysPath = app.Configuration["DataProtection:KeysPath"];
        if (string.IsNullOrWhiteSpace(keysPath))
            throw new InvalidOperationException(
                "DataProtection:KeysPath must point to a persistent volume outside Development; keys kept in memory are lost on restart " +
                "and every encrypted ID number becomes unreadable. Set DataProtection__KeysPath via environment variable.");
        if (!Directory.Exists(keysPath))
            throw new InvalidOperationException($"DataProtection:KeysPath '{keysPath}' does not exist; mount the persistent volume there.");

        var probe = Path.Combine(keysPath, $".write-test-{Guid.NewGuid():N}");
        try
        {
            File.WriteAllBytes(probe, []);
            File.Delete(probe);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new InvalidOperationException($"DataProtection:KeysPath '{keysPath}' is not writable by the application user.", ex);
        }
    }

    private static void WarnIfReverseProxyNotConfigured(WebApplication app)
    {
        if (app.Environment.IsDevelopment())
            return;

        var proxies = app.Configuration.GetSection("ReverseProxy:KnownProxies").Get<string[]>() ?? [];
        var networks = app.Configuration.GetSection("ReverseProxy:KnownNetworks").Get<string[]>() ?? [];
        if (proxies.Length == 0 && networks.Length == 0)
            app.Logger.LogWarning(
                "ReverseProxy:KnownProxies/KnownNetworks are empty. If the API runs behind a reverse proxy, configure them; " +
                "otherwise all clients share the proxy IP for rate limiting.");
    }
}
