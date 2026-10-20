using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using renting_room.Domain.Contracts;
using renting_room.Domain.Properties;
using renting_room.Domain.Renters;

namespace renting_room.Infrastructure.Persistence.Configurations;

internal sealed class ContractRepresentativeConfiguration : IEntityTypeConfiguration<ContractRepresentative>
{
    public void Configure(EntityTypeBuilder<ContractRepresentative> builder)
    {
        builder.ToTable("contract_representatives", t => t.HasCheckConstraint("ck_contract_representatives_dates", "to_date >= from_date"));
        builder.HasKey(h => h.Id);
        builder.ConfigureAuditable();
        builder.HasOne<Renter>()
            .WithMany()
            .HasForeignKey(h => new { h.OrganizationId, h.RenterId })
            .HasPrincipalKey(r => new { r.OrganizationId, r.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(h => new { h.OrganizationId, h.RenterId }).HasDatabaseName("ix_contract_representatives_renter");
    }
}
