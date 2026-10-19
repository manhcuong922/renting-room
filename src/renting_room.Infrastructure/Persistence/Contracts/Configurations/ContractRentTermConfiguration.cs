using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using renting_room.Domain.Contracts;
using renting_room.Domain.Properties;
using renting_room.Domain.Renters;

namespace renting_room.Infrastructure.Persistence.Configurations;

internal sealed class ContractRentTermConfiguration : IEntityTypeConfiguration<ContractRentTerm>
{
    public void Configure(EntityTypeBuilder<ContractRentTerm> builder)
    {
        builder.ToTable("contract_rent_terms", t => t.HasCheckConstraint("ck_contract_rent_terms_positive", "monthly_rent > 0"));
        builder.HasKey(t => t.Id);
        builder.ConfigureAuditable();
        builder.Property(t => t.MonthlyRent).HasColumnType("numeric(18,0)");
        builder.Property(t => t.AddendumNo).HasMaxLength(30);
        builder.Property(t => t.Note).HasMaxLength(500);
        builder.HasIndex(t => new { t.ContractId, t.EffectiveFrom }).IsUnique().HasDatabaseName(DbConstraints.RentTermUnique);
    }
}
