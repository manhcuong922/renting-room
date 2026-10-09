using renting_room.Application.Audit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using renting_room.Application.Common.Interfaces;
using renting_room.Infrastructure.Auditing;
using renting_room.Infrastructure.Jobs;
using renting_room.Infrastructure.Seeding;
using renting_room.Infrastructure.Exports;
using renting_room.Infrastructure.Idempotency;
using renting_room.Infrastructure.Identity;
using renting_room.Infrastructure.Persistence;

namespace renting_room.Infrastructure;

public static class DependencyInjection
{
    public const string ConnectionStringName = "DefaultConnection";

    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString(ConnectionStringName);
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new InvalidOperationException(
                $"Connection string '{ConnectionStringName}' is not configured. " +
                $"Set ConnectionStrings__{ConnectionStringName} via environment variable or user-secrets.");

        services.AddDbContext<AppDbContext>(options => ConfigureDbContext(options, connectionString));
        services.AddScoped<IAppDbContext>(sp => sp.GetRequiredService<AppDbContext>());

        services.AddOptions<DatabaseOptions>().Bind(configuration.GetSection(DatabaseOptions.SectionName));
        services.AddOptions<BootstrapAdminOptions>().Bind(configuration.GetSection(BootstrapAdminOptions.SectionName));
        services.AddOptions<JwtOptions>()
            .Bind(configuration.GetSection(JwtOptions.SectionName))
            .ValidateDataAnnotations()
            .Validate(o => o.SessionAbsoluteDays >= o.RefreshTokenDays,
                "Jwt:SessionAbsoluteDays must be greater than or equal to Jwt:RefreshTokenDays.")
            .ValidateOnStart();

        services.AddMemoryCache();
        services.AddSingleton<JwtSigningKeyProvider>();
        services.AddSingleton<ITokenService, JwtTokenService>();
        services.AddSingleton<IPasswordHasher, PasswordHasher>();
        services.AddScoped<IUserSessionStore, UserSessionStore>();

        services.AddOptions<PersonalDataOptions>()
            .Bind(configuration.GetSection(PersonalDataOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();
        services.AddSingleton<IPersonalDataProtector, PersonalDataProtector>();
        services.AddScoped<IContractNumberGenerator, ContractNumberGenerator>();
        services.AddScoped<IInvoiceLockReader, InvoiceLockReader>();
        services.AddScoped<IDocumentNumberGenerator, DocumentNumberGenerator>();
        services.AddSingleton<ISpreadsheetWriter, ClosedXmlSpreadsheetWriter>();
        services.AddSingleton<IWordDocumentWriter, OpenXmlWordWriter>();
        services.AddSingleton<ISpreadsheetReader, ClosedXmlSpreadsheetReader>();
        services.AddOptions<FeeOptions>().Bind(configuration.GetSection(FeeOptions.SectionName));
        services.AddSingleton<IFeeSettings, FeeSettings>();
        services.AddScoped<IFeePriceLockReader, FeePriceLockReader>();

        services.AddOptions<IdempotencyOptions>()
            .Bind(configuration.GetSection(IdempotencyOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();
        services.AddScoped<IIdempotencyStore, IdempotencyStore>();
        services.AddHostedService<IdempotencyCleanupService>();

        services.AddOptions<AuditOptions>()
            .Bind(configuration.GetSection(AuditOptions.SectionName))
            .ValidateDataAnnotations()
            .Validate(o => o.FlushInterval > TimeSpan.Zero, "Audit:FlushInterval must be positive.")
            .ValidateOnStart();
        services.AddSingleton<AuditQueue>();
        services.AddScoped<IAuditTrail, AuditTrail>();
        services.AddScoped<IAuditLogReader, AuditLogReader>();
        services.AddScoped<IAuditLogEraser, AuditLogEraser>();

        // Job nền chạy dưới danh nghĩa hệ thống của từng tổ chức (CurrentUserOverride) — RT-BR-06 ẩn danh sau 36 tháng.
        services.AddScoped<CurrentUserOverride>();
        services.AddSingleton<RenterAnonymizationService>();
        services.AddHostedService(sp => sp.GetRequiredService<RenterAnonymizationService>());

        // Dữ liệu demo cho dev / server test — chỉ chạy khi bật Seed:DemoData:Enabled (DatabaseInitializer).
        services.AddOptions<DemoDataOptions>().Bind(configuration.GetSection(DemoDataOptions.SectionName));
        services.AddTransient<DemoDataSeeder>();
        services.AddHostedService<AuditLogWriter>();

        return services;
    }

    internal static void ConfigureDbContext(DbContextOptionsBuilder options, string connectionString) =>
        options
            .UseNpgsql(connectionString, npgsql => npgsql.MigrationsHistoryTable("__ef_migrations_history"))
            .UseSnakeCaseNamingConvention();
}
