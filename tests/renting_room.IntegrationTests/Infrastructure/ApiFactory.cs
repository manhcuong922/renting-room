using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Time.Testing;
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

    public string ConnectionString => _database.GetConnectionString();

    public Task InitializeAsync() => _database.StartAsync();

    public new async Task DisposeAsync()
    {
        await base.DisposeAsync();
        await _database.DisposeAsync();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        // UseSetting (không dùng ConfigureAppConfiguration) để giá trị có hiệu lực ngay khi Program đọc cấu hình.
        builder.UseSetting("ConnectionStrings:DefaultConnection", _database.GetConnectionString());
        builder.UseSetting("Database:MigrateOnStartup", "true");
        builder.UseSetting("Jwt:SigningKey", "integration-test-signing-key-at-least-32-bytes!");
        builder.UseSetting("Bootstrap:Admin:Phone", AdminPhone);
        builder.UseSetting("Bootstrap:Admin:Password", AdminPassword);
        ConfigureRateLimits(builder);

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(Clock);
            services.AddSingleton<IStartupFilter, TestClientIpStartupFilter>();
        });
    }

    /// <summary>Mặc định: hạn mức rất cao để test chức năng không bị 429 (limiter vẫn chạy).</summary>
    protected virtual void ConfigureRateLimits(IWebHostBuilder builder)
    {
        foreach (var rule in new[] { "Login", "Refresh", "Sensitive", "Authenticated", "Anonymous", "Write" })
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
    public const int LoginLimit = 3;
    public const int SensitiveLimit = 2;
    public const int WriteLimit = 5;

    protected override void ConfigureRateLimits(IWebHostBuilder builder)
    {
        base.ConfigureRateLimits(builder);
        builder.UseSetting("RateLimiting:Login:PermitLimit", LoginLimit.ToString());
        builder.UseSetting("RateLimiting:Sensitive:PermitLimit", SensitiveLimit.ToString());
        builder.UseSetting("RateLimiting:Write:PermitLimit", WriteLimit.ToString());
    }
}

[CollectionDefinition(Name)]
public sealed class RateLimitedCollection : ICollectionFixture<RateLimitedApiFactory>
{
    public const string Name = "rate-limited";
}
