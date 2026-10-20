using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using renting_room.Domain.Fees;
using renting_room.Domain.Meters;
using renting_room.Domain.Properties;

namespace renting_room.Infrastructure.Persistence.Configurations;

internal sealed class MeterConfiguration : IEntityTypeConfiguration<Meter>
{
    public void Configure(EntityTypeBuilder<Meter> builder)
    {
        builder.ToTable("meters", t =>
            t.HasCheckConstraint("ck_meters_dates", "removed_date IS NULL OR removed_date >= installed_date"));
        builder.HasKey(m => m.Id);
        builder.HasAlternateKey(m => new { m.OrganizationId, m.Id }).HasName("ak_meters_organization_id_id");
        builder.ConfigureAuditable();

        builder.HasOne<Room>()
            .WithMany()
            .HasForeignKey(m => new { m.OrganizationId, m.PropertyId, m.RoomId })
            .HasPrincipalKey(r => new { r.OrganizationId, r.PropertyId, r.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<FeeType>()
            .WithMany()
            .HasForeignKey(m => new { m.OrganizationId, m.PropertyId, m.FeeTypeId })
            .HasPrincipalKey(f => new { f.OrganizationId, f.PropertyId, f.Id })
            .OnDelete(DeleteBehavior.Restrict);

        builder.Property(m => m.SerialNo).HasMaxLength(50);
        builder.Property(m => m.Note).HasMaxLength(300);
        builder.Ignore(m => m.IsActive);
        builder.Ignore(m => m.Ordered);
        builder.Ignore(m => m.Latest);

        builder.HasMany(m => m.Readings).WithOne()
            .HasForeignKey(r => new { r.OrganizationId, r.MeterId })
            .HasPrincipalKey(m => new { m.OrganizationId, m.Id })
            .OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(m => m.Readings).UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasIndex(m => new { m.RoomId, m.FeeTypeId }).IsUnique()
            .HasFilter("removed_date IS NULL").HasDatabaseName(DbConstraints.MeterActiveUnique);
        builder.HasIndex(m => new { m.OrganizationId, m.FeeTypeId }).HasDatabaseName("ix_meters_fee_type");
    }
}
