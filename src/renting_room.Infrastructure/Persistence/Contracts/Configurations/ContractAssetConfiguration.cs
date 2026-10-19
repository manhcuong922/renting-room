using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using renting_room.Domain.Contracts;
using renting_room.Domain.Properties;
using renting_room.Domain.Renters;

namespace renting_room.Infrastructure.Persistence.Configurations;

internal sealed class ContractAssetConfiguration : IEntityTypeConfiguration<ContractAsset>
{
    public void Configure(EntityTypeBuilder<ContractAsset> builder)
    {
        builder.ToTable("contract_assets", t =>
        {
            t.HasCheckConstraint("ck_contract_assets_quantity", "quantity BETWEEN 1 AND 100");
            t.HasCheckConstraint("ck_contract_assets_money",
                "(value_estimate IS NULL OR value_estimate >= 0) AND (compensation_value IS NULL OR compensation_value >= 0)");
        });
        builder.HasKey(a => a.Id);
        builder.ConfigureAuditable();
        builder.Property(a => a.Name).HasMaxLength(200).IsRequired();
        builder.Property(a => a.ConditionAtHandover).HasMaxLength(500);
        builder.Property(a => a.ConditionAtReturn).HasMaxLength(500);
        builder.Property(a => a.ValueEstimate).HasColumnType("numeric(18,0)");
        builder.Property(a => a.Note).HasMaxLength(500);
    }
}
