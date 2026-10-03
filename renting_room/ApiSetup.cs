using System.Net;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.OpenApi.Models;
using renting_room.Application.Common.Interfaces;
using renting_room.Errors;
using renting_room.Idempotency;
using renting_room.Infrastructure.Identity;
using renting_room.Infrastructure.Persistence;
using renting_room.Middleware;
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
            // "version" trả ra dạng chuỗi (xmin) — client gửi lại nguyên chuỗi đó khi cập nhật.
            options.SerializerOptions.NumberHandling = JsonNumberHandling.AllowReadingFromString;
        });

        // JSON sai cú pháp / thiếu body → ném BadHttpRequestException để GlobalExceptionHandler trả ProblemDetails.
        services.Configure<RouteHandlerOptions>(options => options.ThrowOnBadRequest = true);

        services.AddApiProblemDetails();
        services.AddExceptionHandler<GlobalExceptionHandler>();

        services.AddHttpContextAccessor();
        services.AddScoped<ICurrentUser, HttpContextCurrentUser>();
        services.AddJwtAuthentication();
        services.AddAppAuthorization();
        services.AddApiRateLimiting(configuration);

        // Mã hóa dữ liệu nhạy cảm lưu tạm (response idempotency chứa token / mật khẩu tạm).
        // Nhiều instance / container: PHẢI cấu hình DataProtection:KeysPath trỏ tới thư mục dùng chung và bền vững,
        // nếu không mỗi instance có khóa riêng và mất khóa khi container khởi động lại.
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
        // KHÔNG BAO GIỜ xóa trắng KnownProxies/KnownNetworks — khi đó client tự giả IP được và vượt rate limit.
        services.Configure<ForwardedHeadersOptions>(options =>
        {
            options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
            foreach (var proxy in configuration.GetSection("ReverseProxy:KnownProxies").Get<string[]>() ?? [])
                options.KnownProxies.Add(IPAddress.Parse(proxy));
            foreach (var network in configuration.GetSection("ReverseProxy:KnownNetworks").Get<string[]>() ?? [])
                options.KnownNetworks.Add(ParseNetwork(network));
        });

        services.AddHealthChecks().AddDbContextCheck<AppDbContext>("database", tags: [ReadyTag]);

        services.AddEndpointsApiExplorer();
        services.AddSwaggerGen(options =>
        {
            options.SwaggerDoc("v1", new OpenApiInfo { Title = "Renting Room API", Version = "v1" });
            var bearer = new OpenApiSecurityScheme
            {
                Name = "Authorization",
                Type = SecuritySchemeType.Http,
                Scheme = "bearer",
                BearerFormat = "JWT",
                In = ParameterLocation.Header,
                Description = "Dán access token (không cần gõ chữ 'Bearer').",
                Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" }
            };
            options.AddSecurityDefinition("Bearer", bearer);
            options.AddSecurityRequirement(new OpenApiSecurityRequirement { [bearer] = [] });
            options.OperationFilter<IdempotencyHeaderOperationFilter>();
        });

        return services;
    }

    public static WebApplication UseApi(this WebApplication app)
    {
        // Fail-fast: thiếu/sai Jwt:SigningKey ở production phải làm app dừng ngay khi khởi động,
        // không phải đợi tới request đăng nhập đầu tiên mới trả 500.
        _ = app.Services.GetRequiredService<JwtSigningKeyProvider>();
        _ = app.Services.GetRequiredService<IPersonalDataProtector>(); // thiếu PersonalData:HashKey ⇒ dừng ngay
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

    /// <summary>"10.0.0.0/8" → IPNetwork.</summary>
    private static Microsoft.AspNetCore.HttpOverrides.IPNetwork ParseNetwork(string cidr)
    {
        var parts = cidr.Split('/');
        if (parts.Length != 2 || !IPAddress.TryParse(parts[0], out var prefix) || !int.TryParse(parts[1], out var length))
            throw new InvalidOperationException($"ReverseProxy:KnownNetworks entry '{cidr}' is not a valid CIDR.");

        return new Microsoft.AspNetCore.HttpOverrides.IPNetwork(prefix, length);
    }

    /// <summary>
    /// Sau proxy mà không khai báo proxy ⇒ mọi client có chung IP của proxy ⇒ giới hạn đăng nhập theo IP
    /// áp cho TẤT CẢ người dùng cùng lúc. Cảnh báo để người vận hành cấu hình đúng.
    /// </summary>
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
