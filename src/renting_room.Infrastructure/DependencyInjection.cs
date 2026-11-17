using Npgsql;
using renting_room.Application.Audit;
using renting_room.Application.Attachments;
using renting_room.Infrastructure.Storage;
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
using renting_room.Infrastructure.Email;
using renting_room.Infrastructure.Jobs;
using renting_room.Infrastructure.Notifications;
using renting_room.Application.Notifications;
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
        // M01 ID-BR-24: khu được giao của quản lý khu.
        services.AddScoped<IPropertyScopeStore, PropertyScopeStore>();
        services.AddScoped<IPropertyAccessRepository, PropertyAccessRepository>();

        services.AddOptions<PersonalDataOptions>()
            .Bind(configuration.GetSection(PersonalDataOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();
        services.AddSingleton<IPersonalDataProtector, PersonalDataProtector>();
        services.AddScoped<IContractNumberGenerator, ContractNumberGenerator>();
        services.AddScoped<IInvoiceLockReader, InvoiceLockReader>();
        services.AddScoped<IDocumentNumberGenerator, DocumentNumberGenerator>();
        services.AddOptions<EmailOptions>().Bind(configuration.GetSection(EmailOptions.SectionName));
        services.AddSingleton<IEmailSender, SmtpEmailSender>();
        services.AddSingleton<ISpreadsheetWriter, ClosedXmlSpreadsheetWriter>();
        services.AddSingleton<IWordDocumentWriter, OpenXmlWordWriter>();
        services.AddSingleton<ISpreadsheetReader, ClosedXmlSpreadsheetReader>();
        services.AddOptions<FeeOptions>().Bind(configuration.GetSection(FeeOptions.SectionName));
        services.AddSingleton<IFeeSettings, FeeSettings>();
        services.AddScoped<IFeePriceLockReader, FeePriceLockReader>();

        services.AddScoped<IUnitOfWork, UnitOfWork>();
        services.AddScoped<DomainEventPublisher>();
        services.AddScoped<IContractRepository, ContractRepository>();
        services.AddScoped<IContractQueries, ContractQueries>();
        services.AddScoped<IContractTemplateRepository, ContractTemplateRepository>();
        services.AddScoped<IContractTemplateQueries, ContractTemplateQueries>();
        services.AddScoped<IExportQueries, ExportQueries>();
        services.AddScoped<IFeeTypeRepository, FeeTypeRepository>();
        services.AddScoped<IFeeQueries, FeeQueries>();
        services.AddScoped<IInvoiceRepository, InvoiceRepository>();
        services.AddScoped<IInvoiceQueries, InvoiceQueries>();
        services.AddScoped<IBillingGapRepository, BillingGapRepository>();
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
            .ValidateOnStart();
        services.AddScoped<IAuditTrail, AuditTrail>();
        services.AddScoped<IAuditLogReader, AuditLogReader>();
        services.AddScoped<IAuditLogEraser, AuditLogEraser>();
        services.AddScoped<IPersonalNoteEraser, PersonalNoteEraser>();

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

        AddNotifications(services, configuration);
        AddStorage(services, configuration);

        // Dữ liệu demo cho dev / server test — chỉ chạy khi bật Seed:DemoData:Enabled (DatabaseInitializer).
        services.AddOptions<DemoDataOptions>().Bind(configuration.GetSection(DemoDataOptions.SectionName));
        services.AddTransient<DemoDataSeeder>();

        return services;
    }

    /// <summary>
    /// M11: gửi phiếu / nhắc nợ qua Zalo Bot + email. Thiếu token Zalo hoặc SMTP KHÔNG làm app ngừng chạy — kênh đó chỉ ở trạng thái "chưa cấu hình".
    /// </summary>
    private static void AddNotifications(IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<ZaloBotOptions>()
            .Bind(configuration.GetSection(ZaloBotOptions.SectionName))
            .ValidateDataAnnotations()
            .Validate(o => ZaloBotOptions.IsValidToken(o.Token), "ZaloBot:Token must look like '<digits>:<secret>' (the token Zalo gives when creating the bot).")
            .ValidateOnStart();
        services.AddOptions<NotificationOptions>()
            .Bind(configuration.GetSection(NotificationOptions.SectionName))
            .ValidateDataAnnotations()
            .Validate(o => o.SendWindowStartHour < o.SendWindowEndHour, "Notifications:SendWindowStartHour must be before SendWindowEndHour.")
            .ValidateOnStart();

        // Tắt log URL của HttpClient: đường dẫn gọi Zalo chứa token.
        services.AddHttpClient(ZaloBotClient.HttpClientName, client => client.Timeout = TimeSpan.FromSeconds(90)).RemoveAllLoggers();
        services.AddSingleton<IZaloBotClient, ZaloBotClient>();
        services.AddSingleton<INotificationChannels, NotificationChannels>();
        services.AddSingleton<ILinkCodeHasher, LinkCodeHasher>();
        services.AddSingleton<ZaloLinkAttemptLimiter>();
        services.AddSingleton<ZaloBotMessageHandler>();
        services.AddSingleton<INotificationSender, ZaloNotificationSender>();
        services.AddSingleton<INotificationSender, EmailNotificationSender>();

        services.AddScoped<INotificationRepository, NotificationRepository>();
        services.AddScoped<IZaloLinkCodeRepository, ZaloLinkCodeRepository>();
        services.AddScoped<IZaloLinkLookup, ZaloLinkLookup>();
        services.AddScoped<NotificationOutboxStore>();

        services.AddSingleton<NotificationDispatcher>();
        services.AddHostedService(sp => sp.GetRequiredService<NotificationDispatcher>());
        services.AddSingleton<ZaloBotUpdatePoller>();
        services.AddHostedService(sp => sp.GetRequiredService<ZaloBotUpdatePoller>());
        services.AddSingleton<OverdueReminderScheduler>();
        services.AddHostedService(sp => sp.GetRequiredService<OverdueReminderScheduler>());
        services.AddSingleton<NotificationCleanupService>();
        services.AddHostedService(sp => sp.GetRequiredService<NotificationCleanupService>());
    }

    /// <summary>
    /// M09: lưu ảnh / tài liệu. Mặc định Local (không cần MinIO). <c>Storage:Provider=S3</c> thiếu cấu hình ⇒ app không khởi động (FS-BR-13);
    /// không kết nối MinIO lúc khởi động (FS-BR-12).
    /// </summary>
    private static void AddStorage(IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<StorageOptions>()
            .Bind(configuration.GetSection(StorageOptions.SectionName))
            .ValidateOnStart();
        // Validator riêng để thông báo lỗi nêu đúng khóa còn thiếu (VD Storage:S3:SecretKey).
        services.AddSingleton<Microsoft.Extensions.Options.IValidateOptions<StorageOptions>, StorageOptionsValidator>();
        services.AddSingleton<IFileStorageSelector, FileStorageSelector>();
        services.AddSingleton<IStorageSettings, StorageSettings>();
        services.AddScoped<IAttachmentRepository, AttachmentRepository>();
        services.AddScoped<IStorageQuota, StorageQuota>();
        services.AddScoped<IAttachmentOwners, AttachmentOwners>();
        // FS-11: mỗi ngày xóa file vật lý của tệp xóa mềm quá Storage:PurgeAfterDays và trả hạn mức.
        services.AddSingleton<AttachmentPurgeService>();
        services.AddHostedService(sp => sp.GetRequiredService<AttachmentPurgeService>());
    }

    private sealed class StorageOptionsValidator : Microsoft.Extensions.Options.IValidateOptions<StorageOptions>
    {
        public Microsoft.Extensions.Options.ValidateOptionsResult Validate(string? name, StorageOptions options)
        {
            var errors = options.Validate();
            return errors.Count == 0
                ? Microsoft.Extensions.Options.ValidateOptionsResult.Success
                : Microsoft.Extensions.Options.ValidateOptionsResult.Fail(errors);
        }
    }

    internal static void ConfigureDbContext(DbContextOptionsBuilder options, string connectionString) =>
        options
            .UseNpgsql(WithoutErrorDetail(connectionString), npgsql => npgsql.MigrationsHistoryTable("__ef_migrations_history"))
            .UseSnakeCaseNamingConvention();

    /// <summary>
    /// L-12 (05/11/2026): <c>Include Error Detail</c> làm <c>PostgresException.Detail</c> chứa giá trị của dòng lỗi ("Failing row contains
    /// (…, họ tên, SĐT, …)") và đi thẳng vào log ⇒ luôn tắt, kể cả khi chuỗi kết nối bật.
    /// </summary>
    private static string WithoutErrorDetail(string connectionString) =>
        new NpgsqlConnectionStringBuilder(connectionString) { IncludeErrorDetail = false }.ConnectionString;
}
