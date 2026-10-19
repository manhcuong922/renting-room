using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using renting_room.Domain.Contracts;
using renting_room.Domain.Properties;
using renting_room.Domain.Renters;

namespace renting_room.Infrastructure.Persistence.Configurations;

internal sealed class ContractOccupantConfiguration : IEntityTypeConfiguration<ContractOccupant>
{
    public void Configure(EntityTypeBuilder<ContractOccupant> builder)
    {
        builder.ToTable("contract_occupants", t =>
            t.HasCheckConstraint("ck_contract_occupants_dates", "move_out_date IS NULL OR move_out_date >= move_in_date"));
        builder.HasKey(o => o.Id);
        builder.ConfigureAuditable();

        builder.HasOne<Renter>()
            .WithMany()
            .HasForeignKey(o => new { o.OrganizationId, o.RenterId })
            .HasPrincipalKey(r => new { r.OrganizationId, r.Id })
            .OnDelete(DeleteBehavior.Restrict);

        builder.Property(o => o.Relationship).HasMaxLength(50);
        builder.Property(o => o.RelationshipType).HasConversion<string>().HasMaxLength(24);
        builder.Property(o => o.Role).HasConversion<string>().HasMaxLength(10).HasDefaultValue(OccupantRole.Resident);
        builder.Property(o => o.Note).HasMaxLength(500);
        builder.HasIndex(o => new { o.OrganizationId, o.RenterId }).HasDatabaseName("ix_contract_occupants_renter");
    }
}
