using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using renting_room.Domain.Identity;

namespace renting_room.Infrastructure.Persistence.Configurations;

internal sealed class OrganizationConfiguration : IEntityTypeConfiguration<Organization>
{
    public void Configure(EntityTypeBuilder<Organization> builder)
    {
        builder.ToTable("organizations", t => t.HasCheckConstraint(
            "ck_organizations_suspended_reason",
            "status <> 'Suspended' OR suspended_reason IS NOT NULL"));

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

        builder.HasIndex(o => o.Code).IsUnique().HasDatabaseName(DbConstraints.OrganizationCodeUnique);
    }
}
