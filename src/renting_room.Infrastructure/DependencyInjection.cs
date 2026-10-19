using renting_room.Application.Audit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using renting_room.Application.Common.Interfaces;
using renting_room.Application.Billing;
using renting_room.Application.Contracts;
using renting_room.Application.Fees;
using renting_room.Application.Identity;
using renting_room.Application.Identity.Organizations;
using renting_room.Application.Meters;
using renting_room.Application.Payments;
using renting_room.Application.Properties;
using renting_room.Application.Renters;
using renting_room.Application.Rooms;
using renting_room.Infrastructure.Auditing;
using renting_room.Infrastructure.Jobs;
using renting_room.Infrastructure.Seeding;
using renting_room.Application.Exports;
using renting_room.Infrastructure.Exports;
using renting_room.Infrastructure.Fees;
using renting_room.Infrastructure.Idempotency;
using renting_room.Infrastructure.Identity;
using renting_room.Infrastructure.Persistence;
using renting_room.Infrastructure.Persistence.Queries;
using renting_room.Infrastructure.Persistence.Repositories;

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

        services.AddScoped<IUnitOfWork, UnitOfWork>();
        services.AddScoped<IContractRepository, ContractRepository>();
        services.AddScoped<IContractQueries, ContractQueries>();
        services.AddScoped<IContractTemplateRepository, ContractTemplateRepository>();
        services.AddScoped<IContractTemplateQueries, ContractTemplateQueries>();
        services.AddScoped<IExportQueries, ExportQueries>();
        services.AddScoped<IFeeTypeRepository, FeeTypeRepository>();
        services.AddScoped<IFeeQueries, FeeQueries>();
        services.AddScoped<IInvoiceRepository, InvoiceRepository>();
        services.AddScoped<IInvoiceQueries, InvoiceQueries>();
        services.AddScoped<IMeterRepository, MeterRepository>();
        services.AddScoped<IMeterQueries, MeterQueries>();
        services.AddScoped<IOrganizationRepository, OrganizationRepository>();
        services.AddScoped<IOrganizationQueries, OrganizationQueries>();
        services.AddScoped<IPaymentRepository, PaymentRepository>();
        services.AddScoped<IPaymentQueries, PaymentQueries>();
        services.AddScoped<IPropertyRepository, PropertyRepository>();
        services.AddScoped<IPropertyQueries, PropertyQueries>();
        services.AddScoped<IRenterRepository, RenterRepository>();
        services.AddScoped<IRenterConsentRepository, RenterConsentRepository>();
        services.AddScoped<IRenterQueries, RenterQueries>();
        services.AddScoped<IRoomChargeRepository, RoomChargeRepository>();
        services.AddScoped<IRoomChargeQueries, RoomChargeQueries>();
        services.AddScoped<IRoomRepository, RoomRepository>();
        services.AddScoped<IRoomGroupRepository, RoomGroupRepository>();
        services.AddScoped<IRoomQueries, RoomQueries>();
        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<IRefreshTokenRepository, RefreshTokenRepository>();
        services.AddScoped<ILoginEventRepository, LoginEventRepository>();
        services.AddScoped<IUserQueries, UserQueries>();

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
        // S2: xóa nhật ký cũ hơn Audit:RetentionYears (mặc định 5 năm).
        services.AddSingleton<AuditRetentionService>();
        services.AddHostedService(sp => sp.GetRequiredService<AuditRetentionService>());
        // M01: dọn refresh token hết hạn và nhật ký đăng nhập cũ.
        services.AddSingleton<AccountCleanupService>();
        services.AddHostedService(sp => sp.GetRequiredService<AccountCleanupService>());

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
