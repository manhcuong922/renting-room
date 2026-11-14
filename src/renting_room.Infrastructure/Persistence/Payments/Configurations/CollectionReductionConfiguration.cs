using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using renting_room.Domain.Billing;
using renting_room.Domain.Payments;

namespace renting_room.Infrastructure.Persistence.Configurations;

/// <summary>M08 PM-BR-38 (8): lịch sử giảm số đã thu — chỉ ghi.</summary>
internal sealed class CollectionReductionConfiguration : IEntityTypeConfiguration<CollectionReduction>
{
    public void Configure(EntityTypeBuilder<CollectionReduction> builder)
    {
        builder.ToTable("collection_reductions",
            t => t.HasCheckConstraint("ck_collection_reductions_amounts", "amount_after >= 0 AND amount_after < amount_before"));
        builder.HasKey(r => r.Id);
        builder.ConfigureAuditable();

        builder.HasOne<Invoice>()
            .WithMany()
            .HasForeignKey(r => new { r.OrganizationId, r.ContractId, r.InvoiceId })
            .HasPrincipalKey(i => new { i.OrganizationId, i.ContractId, i.Id })
            .OnDelete(DeleteBehavior.Restrict);

        builder.Property(r => r.AmountBefore).HasColumnType("numeric(18,0)");
        builder.Property(r => r.AmountAfter).HasColumnType("numeric(18,0)");
        builder.Property(r => r.Reason).HasMaxLength(500).IsRequired();
        builder.Ignore(r => r.Amount);
        builder.HasIndex(r => new { r.OrganizationId, r.InvoiceId }, "ix_collection_reductions_invoice").HasDatabaseName("ix_collection_reductions_invoice");
    }
}
