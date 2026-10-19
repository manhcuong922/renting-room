using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using renting_room.Domain.Billing;
using renting_room.Domain.Identity;
using renting_room.Domain.Properties;

namespace renting_room.Infrastructure.Persistence.Configurations;

internal sealed class PropertyConfiguration : IEntityTypeConfiguration<Property>
{
    public void Configure(EntityTypeBuilder<Property> builder)
    {
        builder.ToTable("properties", t =>
        {
            t.HasCheckConstraint("ck_properties_anchor_day", "billing_anchor_day BETWEEN 1 AND 28");
            t.HasCheckConstraint("ck_properties_lessor_organization",
                "lessor_type IS DISTINCT FROM 'Organization' OR (lessor_tax_code IS NOT NULL AND lessor_representative_name IS NOT NULL)");
        });

        builder.HasKey(p => p.Id);
        // Đích của FK composite (C-01): bảng con trỏ tới (organization_id, id) ⇒ DB chặn liên kết chéo tổ chức.
        builder.HasAlternateKey(p => new { p.OrganizationId, p.Id }).HasName("ak_properties_organization_id_id");
        builder.ConfigureAuditable();

        builder.HasOne<Organization>().WithMany().HasForeignKey(p => p.OrganizationId).OnDelete(DeleteBehavior.Restrict);

        builder.Property(p => p.Code).HasMaxLength(32).IsRequired();
        builder.Property(p => p.Name).HasMaxLength(200).IsRequired();
        builder.Property(p => p.StreetAddress).HasMaxLength(300).IsRequired();
        builder.Property(p => p.CommuneName).HasMaxLength(100).IsRequired();
        builder.Property(p => p.ProvinceName).HasMaxLength(100).IsRequired();
        builder.Property(p => p.CommuneCode).HasMaxLength(10);
        builder.Property(p => p.ProvinceCode).HasMaxLength(10);
        builder.Property(p => p.Description).HasMaxLength(2000);
        builder.Property(p => p.EvnCustomerCode).HasMaxLength(20);
        builder.Property(p => p.ChargeMode).HasConversion<string>().HasMaxLength(16);
        builder.Property(p => p.ProrationMode).HasConversion<string>().HasMaxLength(16);
        builder.Property(p => p.RoundInvoiceTotal);
        // Lịch kỳ thu (K4): danh sách mốc đổi ngày chốt / cách thu — jsonb, luôn đọc cùng khu.
        builder.Property(p => p.BillingScheduleEntries)
            .HasColumnName("billing_schedule")
            .HasColumnType("jsonb")
            .HasConversion(
                v => JsonSerializer.Serialize(v, PropertyJson.Schedule),
                s => JsonSerializer.Deserialize<BillingScheduleEntry[]>(s, PropertyJson.Schedule) ?? Array.Empty<BillingScheduleEntry>(),
                new ValueComparer<IReadOnlyList<BillingScheduleEntry>>(
                    (a, b) => a!.SequenceEqual(b!),
                    v => v.Aggregate(0, (h, e) => HashCode.Combine(h, e.GetHashCode())),
                    v => v.ToArray()));

        builder.Property(p => p.LessorType).HasConversion<string>().HasMaxLength(16);
        builder.Property(p => p.LessorName).HasMaxLength(200);
        builder.Property(p => p.LessorAddress).HasMaxLength(500);
        builder.Property(p => p.LessorPhone).HasMaxLength(15);
        builder.Property(p => p.LessorEmail).HasMaxLength(254);
        builder.Property(p => p.LessorIdType).HasConversion<string>().HasMaxLength(16);
        builder.Property(p => p.LessorIdNumberLast4).HasMaxLength(4);
        builder.Property(p => p.LessorIdIssuePlace).HasMaxLength(200);
        builder.Property(p => p.LessorTaxCode).HasMaxLength(14);
        builder.Property(p => p.LessorRepresentativeName).HasMaxLength(200);
        builder.Property(p => p.LessorRepresentativeTitle).HasMaxLength(100);
        builder.Property(p => p.AuthorizationDocNo).HasMaxLength(50);
        builder.Property(p => p.LandParcelNo).HasMaxLength(20);
        builder.Property(p => p.LandMapSheetNo).HasMaxLength(20);
        builder.Property(p => p.OwnershipCertificateNo).HasMaxLength(50);
        builder.Property(p => p.BankName).HasMaxLength(100);
        builder.Property(p => p.BankAccountNo).HasMaxLength(20);
        builder.Property(p => p.BankAccountName).HasMaxLength(200);
        builder.Property(p => p.HouseRulesText).HasMaxLength(20_000);

        builder.Ignore(p => p.BillingSettings);
        builder.Ignore(p => p.BillingSchedule);
        builder.Ignore(p => p.Address);
        builder.Ignore(p => p.Lessor);
        builder.Ignore(p => p.BankAccount);

        builder.HasIndex(p => new { p.OrganizationId, p.Code }).IsUnique().HasDatabaseName(DbConstraints.PropertyCodeUnique);
    }
}
