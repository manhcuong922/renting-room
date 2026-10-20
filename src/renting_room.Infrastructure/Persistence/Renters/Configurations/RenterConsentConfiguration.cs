using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using renting_room.Domain.Renters;

namespace renting_room.Infrastructure.Persistence.Configurations;

internal sealed class RenterConsentConfiguration : IEntityTypeConfiguration<RenterConsent>
{
    public void Configure(EntityTypeBuilder<RenterConsent> builder)
    {
        builder.ToTable("renter_consents", t =>
            t.HasCheckConstraint("ck_renter_consents_dates", "withdrawn_at IS NULL OR withdrawn_at >= given_at"));
        builder.HasKey(c => c.Id);
        builder.ConfigureAuditable();

        builder.HasOne<Renter>()
            .WithMany()
            .HasForeignKey(c => new { c.OrganizationId, c.RenterId })
            .HasPrincipalKey(r => new { r.OrganizationId, r.Id })
            .OnDelete(DeleteBehavior.Restrict);

        builder.Property(c => c.Purpose).HasMaxLength(100).IsRequired();
        builder.Property(c => c.Method).HasConversion<string>().HasMaxLength(20);
        builder.Property(c => c.Note).HasMaxLength(500);
        builder.HasIndex(c => new { c.OrganizationId, c.RenterId }).HasDatabaseName("ix_renter_consents_renter");
    }
}
