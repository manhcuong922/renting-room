using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using renting_room.Domain.Contracts;
using renting_room.Domain.Fees;
using renting_room.Domain.Renters;

namespace renting_room.Infrastructure.Persistence.Configurations;

internal sealed class ContractFeeConfiguration : IEntityTypeConfiguration<ContractFee>
{
    public void Configure(EntityTypeBuilder<ContractFee> builder)
    {
        builder.ToTable("contract_fees", t =>
        {
            t.HasCheckConstraint("ck_contract_fees_values",
                "quantity > 0 AND (unit_price_override IS NULL OR unit_price_override >= 0)");
            t.HasCheckConstraint("ck_contract_fees_dates", "effective_to IS NULL OR effective_to >= effective_from");
        });
        builder.HasKey(f => f.Id);
        builder.ConfigureAuditable();

        builder.HasOne<FeeType>()
            .WithMany()
            .HasForeignKey(f => new { f.OrganizationId, f.PropertyId, f.FeeTypeId })
            .HasPrincipalKey(t => new { t.OrganizationId, t.PropertyId, t.Id })
            .OnDelete(DeleteBehavior.Restrict);

        builder.Property(f => f.Quantity).HasColumnType("numeric(12,2)");
        builder.Property(f => f.UnitPriceOverride).HasColumnType("numeric(18,2)");
        builder.HasIndex(f => new { f.OrganizationId, f.FeeTypeId }).HasDatabaseName("ix_contract_fees_fee_type");
        // EXCLUDE (contract_id, fee_type_id, daterange) — tạo bằng SQL trong migration (CT-BR-06).
    }
}
