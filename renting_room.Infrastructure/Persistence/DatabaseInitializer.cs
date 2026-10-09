using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;
using renting_room.Application.Common.Interfaces;
using renting_room.Domain.Identity;

using renting_room.Infrastructure.Seeding;

namespace renting_room.Infrastructure.Persistence;

public sealed class DatabaseOptions
{
    public const string SectionName = "Database";

    /// <summary>Tự chạy migration khi khởi động. Bật ở Development; production nên chạy migration trong pipeline deploy.</summary>
    public bool MigrateOnStartup { get; init; }
}

/// <summary>Thông tin SystemAdmin đầu tiên — đặt qua user-secrets (dev) hoặc biến môi trường (prod).</summary>
public sealed class BootstrapAdminOptions
{
    public const string SectionName = "Bootstrap:Admin";
    public const int MinPasswordLength = 12;

    public string FullName { get; init; } = "System Administrator";
    public string? Phone { get; init; }
    public string? Email { get; init; }
    public string? Password { get; init; }
}

public static class DatabaseInitializer
{
    public static async Task InitializeDatabaseAsync(this IServiceProvider services, CancellationToken cancellationToken = default)
    {
        await using var scope = services.CreateAsyncScope();
        var provider = scope.ServiceProvider;
        var logger = provider.GetRequiredService<ILoggerFactory>().CreateLogger(nameof(DatabaseInitializer));
        var db = provider.GetRequiredService<AppDbContext>();

        if (provider.GetRequiredService<IOptions<DatabaseOptions>>().Value.MigrateOnStartup)
        {
            logger.LogInformation("Applying database migrations");
            await db.Database.MigrateAsync(cancellationToken);
        }

        await SeedSystemAdminAsync(
            db,
            provider.GetRequiredService<IPasswordHasher>(),
            provider.GetRequiredService<IOptions<BootstrapAdminOptions>>().Value,
            logger,
            cancellationToken);

        if (provider.GetRequiredService<IOptions<DemoDataOptions>>().Value.Enabled)
            await provider.GetRequiredService<DemoDataSeeder>().SeedAsync(cancellationToken);
    }

    private static async Task SeedSystemAdminAsync(
        AppDbContext db,
        IPasswordHasher passwordHasher,
        BootstrapAdminOptions options,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        if (await db.Users.AnyAsync(u => u.Role == UserRole.SystemAdmin, cancellationToken))
            return;

        if (string.IsNullOrWhiteSpace(options.Password))
        {
            logger.LogWarning(
                "No SystemAdmin exists and Bootstrap:Admin:Password is not configured; skipping admin seeding");
            return;
        }

        if (options.Password.Length < BootstrapAdminOptions.MinPasswordLength)
            throw new InvalidOperationException(
                $"Bootstrap:Admin:Password must be at least {BootstrapAdminOptions.MinPasswordLength} characters.");

        var admin = User.CreateSystemAdmin(
            options.FullName, options.Phone, options.Email, passwordHasher.Hash(options.Password));

        db.Users.Add(admin);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            // Nhiều instance khởi động cùng lúc: instance khác đã seed trước — không phải lỗi.
            logger.LogInformation("SystemAdmin was seeded concurrently by another instance");
            return;
        }

        // Không ghi SĐT/email ra log (dữ liệu cá nhân). Sau lần chạy đầu nên xóa Bootstrap:Admin:Password khỏi môi trường.
        logger.LogInformation("Seeded SystemAdmin {UserId}", admin.Id);
    }
}
