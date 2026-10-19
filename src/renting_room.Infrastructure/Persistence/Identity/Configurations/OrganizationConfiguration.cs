using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using renting_room.Domain.Identity;
using renting_room.Domain.Properties;

namespace renting_room.Infrastructure.Persistence.Configurations;

internal sealed class OrganizationConfiguration : IEntityTypeConfiguration<Organization>
{
    public void Configure(EntityTypeBuilder<Organization> builder)
    {
        builder.ToTable("organizations", t =>
        {
            t.HasCheckConstraint("ck_organizations_suspended_reason", "status <> 'Suspended' OR suspended_reason IS NOT NULL");
            t.HasCheckConstraint("ck_organizations_max_managers", "max_managers BETWEEN 0 AND 100");
            t.HasCheckConstraint("ck_organizations_retention_months", "personal_data_retention_months BETWEEN 36 AND 120");
        });

        builder.HasKey(o => o.Id);
        builder.ConfigureAuditable();

        builder.Property(o => o.Code).HasMaxLength(32).IsRequired();
        builder.Property(o => o.Name).HasMaxLength(200).IsRequired();
        builder.Property(o => o.ContactName).HasMaxLength(200);
        builder.Property(o => o.ContactPhone).HasMaxLength(15);
        builder.Property(o => o.ContactEmail).HasMaxLength(254);
        builder.Property(o => o.TaxCode).HasMaxLength(14);
        builder.Property(o => o.Status).HasConversion<string>().HasMaxLength(16).IsRequired();
        builder.Property(o => o.SuspendedReason).HasMaxLength(500);
        builder.Property(o => o.Note).HasMaxLength(2000);
        builder.Property(o => o.Address).HasMaxLength(500);
        builder.Property(o => o.MaxManagers).HasDefaultValue(Organization.DefaultMaxManagers);
        builder.Property(o => o.PersonalDataRetentionMonths).HasDefaultValue(Organization.DefaultRetentionMonths);
        // Sentinel true: EF luôn gửi giá trị false (tắt) thay vì để DB điền mặc định; tổ chức cũ khi thêm cột nhận true.
        builder.Property(o => o.AutoAnonymizeEnabled).HasDefaultValue(true).HasSentinel(true);

        // PR-BR-17: bên cho thuê mặc định (thông tin chủ trọ) — jsonb, số giấy tờ vẫn ở dạng mã hóa.
        builder.Property(o => o.DefaultLessor)
            .HasColumnType("jsonb")
            .HasConversion(
                v => JsonSerializer.Serialize(v, LessorJson),
                s => JsonSerializer.Deserialize<LessorDetails>(s, LessorJson),
                new ValueComparer<LessorDetails?>(
                    (a, b) => JsonSerializer.Serialize(a, LessorJson) == JsonSerializer.Serialize(b, LessorJson),
                    v => JsonSerializer.Serialize(v, LessorJson).GetHashCode(),
                    v => v));

        builder.HasIndex(o => o.Code).IsUnique().HasDatabaseName(DbConstraints.OrganizationCodeUnique);
    }

    private static readonly JsonSerializerOptions LessorJson = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };
}
