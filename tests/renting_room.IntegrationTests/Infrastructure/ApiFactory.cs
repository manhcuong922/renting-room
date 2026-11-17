using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Time.Testing;
using renting_room.Application.Common.Interfaces;
using renting_room.Application.Notifications;
using renting_room.Domain.Identity;
using renting_room.Infrastructure.Persistence;
using Testcontainers.PostgreSql;

namespace renting_room.IntegrationTests.Infrastructure;

/// <summary>
/// Chạy toàn bộ API trong bộ nhớ trên một PostgreSQL thật (container tạm, tự xóa khi xong).
/// Dùng PostgreSQL thật vì hệ thống dựa vào partial unique index, xmin, ON CONFLICT, ExecuteUpdate — InMemory/SQLite không mô phỏng được.
/// </summary>
public class ApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    public const string AdminPhone = "0900000099";
    public const string AdminPassword = "Admin@Test2026!";

    /// <summary>Header test-only để giả lập IP client (TestServer không có IP thật).</summary>
    public const string TestClientIpHeader = "X-Test-Client-Ip";

    private readonly PostgreSqlContainer _database = new PostgreSqlBuilder("postgres:16-alpine").Build();

    /// <summary>Đồng hồ giả để kiểm thử các mốc thời gian (grace period, lockout) mà không phải chờ thật.</summary>
    public FakeTimeProvider Clock { get; } = new(DateTimeOffset.UtcNow);

    /// <summary>Thư hệ thống gửi đi (M-14: mật khẩu tạm gửi qua email của chủ trọ) — không gửi SMTP thật trong test.</summary>
    public FakeEmailSender Emails { get; } = new();

    /// <summary>Zalo Bot giả (M11) — không gọi mạng thật.</summary>
    public FakeZaloBotClient Zalo { get; } = new();

    public string ConnectionString => _database.GetConnectionString();

    /// <summary>Thư mục lưu file của <c>LocalFileStorage</c> trong test (M09).</summary>
    public string StorageRoot { get; } = Path.Combine(Path.GetTempPath(), "renting_room_tests_storage", Guid.NewGuid().ToString("N"));

    public Task InitializeAsync() => _database.StartAsync();

    public new async Task DisposeAsync()
    {
        await base.DisposeAsync();
        await _database.DisposeAsync();
    }

    /// <summary>
    /// Admin bootstrap bị bắt đổi mật khẩu ở lần đăng nhập đầu (M-18). Các test dùng chung <see cref="AdminPassword"/> nên đánh dấu đã đổi ngay
    /// sau khi seed; hành vi bắt buộc đổi được kiểm riêng ở <c>BootstrapAdminTests</c>.
    /// </summary>
    protected override IHost CreateHost(IHostBuilder builder)
    {
        var host = base.CreateHost(builder);
        using var scope = host.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<AppDbContext>().Users
            .Where(u => u.Role == UserRole.SystemAdmin && u.PhoneNormalized == AdminPhone)
            .ExecuteUpdate(s => s.SetProperty(u => u.MustChangePassword, false));
        return host;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        // UseSetting (không dùng ConfigureAppConfiguration) để giá trị có hiệu lực ngay khi Program đọc cấu hình.
        builder.UseSetting("ConnectionStrings:DefaultConnection", _database.GetConnectionString());
        builder.UseSetting("Database:MigrateOnStartup", "true");
        builder.UseSetting("Jwt:SigningKey", "integration-test-signing-key-at-least-32-bytes!");
        builder.UseSetting("PersonalData:HashKey", "integration-test-personal-data-hmac-key-32+chars");
        builder.UseSetting("Bootstrap:Admin:Phone", AdminPhone);
        builder.UseSetting("Bootstrap:Admin:Password", AdminPassword);
        builder.UseSetting("DataProtection:KeysPath", Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "renting_room_tests_dp_keys")).FullName); // M-17: ngoài Development bắt buộc có, thư mục phải tồn tại
        // M09: lưu file vào thư mục tạm riêng (Provider mặc định Local) — không cần MinIO để chạy test.
        builder.UseSetting("Storage:Local:RootPath", StorageRoot);
        builder.UseSetting("AllowedHosts", "localhost"); // TestServer gửi Host: localhost; ngoài Development "*" bị chặn (L-15)
        ConfigureRateLimits(builder);
        // M11: vòng nền tắt — test tự gọi RunOnceAsync / RunAsync cho tất định; token giả đúng dạng để ValidateOnStart qua.
        builder.UseSetting("ZaloBot:Token", "123456:integration-test-token");
        builder.UseSetting("ZaloBot:PollerEnabled", "false");
        builder.UseSetting("Notifications:DispatcherEnabled", "false");
        builder.UseSetting("Notifications:ReminderSchedulerEnabled", "false");
        builder.UseSetting("Notifications:ZaloPerSecond", "1000");
        builder.UseSetting("Notifications:EmailPerSecond", "1000");

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(Clock);
            services.RemoveAll<IEmailSender>();
            services.AddSingleton<IEmailSender>(Emails);
            services.RemoveAll<IZaloBotClient>();
            services.AddSingleton<IZaloBotClient>(Zalo);
            services.AddSingleton<IStartupFilter, TestClientIpStartupFilter>();
        });
    }

    /// <summary>Mặc định: hạn mức rất cao để test chức năng không bị 429 (limiter vẫn chạy).</summary>
    protected virtual void ConfigureRateLimits(IWebHostBuilder builder)
    {
        foreach (var rule in new[] { "Login", "LoginPerAccount", "Refresh", "Sensitive", "IdLookup", "Authenticated", "Anonymous", "Write" })
            builder.UseSetting($"RateLimiting:{rule}:PermitLimit", "100000");
        builder.UseSetting("RateLimiting:MaxConcurrentRequestsPerClient", "1000");
    }

    private sealed class TestClientIpStartupFilter : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use((context, nextMiddleware) =>
            {
                if (IPAddress.TryParse(context.Request.Headers[TestClientIpHeader], out var ip))
                    context.Connection.RemoteIpAddress = ip;
                return nextMiddleware(context);
            });
            next(app);
        };
    }
}

[CollectionDefinition(Name)]
public sealed class ApiCollection : ICollectionFixture<ApiFactory>
{
    public const string Name = "api";
}

/// <summary>Hạn mức thấp để kiểm thử chống spam (container riêng, không ảnh hưởng test khác).</summary>
public sealed class RateLimitedApiFactory : ApiFactory
{
    public const int LoginLimit = 5;
    public const int LoginPerAccountLimit = 2;
    public const int SensitiveLimit = 2;
    public const int WriteLimit = 8;
    /// <summary>M-29: giới hạn song song thấp + hàng chờ ⇒ request dư xếp hàng thay vì bị 429.</summary>
    public const int ConcurrentLimit = 2;
    public const int ConcurrentQueue = 50;

    protected override void ConfigureRateLimits(IWebHostBuilder builder)
    {
        base.ConfigureRateLimits(builder);
        builder.UseSetting("RateLimiting:Login:PermitLimit", LoginLimit.ToString());
        builder.UseSetting("RateLimiting:LoginPerAccount:PermitLimit", LoginPerAccountLimit.ToString());
        builder.UseSetting("RateLimiting:Sensitive:PermitLimit", SensitiveLimit.ToString());        builder.UseSetting("RateLimiting:Write:PermitLimit", WriteLimit.ToString());
        builder.UseSetting("RateLimiting:MaxConcurrentRequestsPerClient", ConcurrentLimit.ToString());
        builder.UseSetting("RateLimiting:ConcurrentQueueLimit", ConcurrentQueue.ToString());
    }
}

[CollectionDefinition(Name)]
public sealed class RateLimitedCollection : ICollectionFixture<RateLimitedApiFactory>
{
    public const string Name = "rate-limited";
}
