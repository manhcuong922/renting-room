using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using renting_room.Domain.Billing;

namespace renting_room.Infrastructure.Persistence.Configurations;

internal sealed class InvoiceAdjustmentConfiguration : IEntityTypeConfiguration<InvoiceAdjustment>
{
    public void Configure(EntityTypeBuilder<InvoiceAdjustment> builder)
    {
        builder.ToTable("invoice_adjustments");
        builder.HasKey(a => a.Id);
        builder.ConfigureAuditable();

        builder.HasOne<Invoice>().WithMany()
            .HasForeignKey(a => new { a.OrganizationId, a.InvoiceId }).HasPrincipalKey(i => new { i.OrganizationId, i.Id })
            .OnDelete(DeleteBehavior.Restrict);

        builder.Property(a => a.OldValue).HasColumnType("numeric(12,2)");
        builder.Property(a => a.NewValue).HasColumnType("numeric(12,2)");
        builder.Property(a => a.OldQuantity).HasColumnType("numeric(12,2)");
        builder.Property(a => a.NewQuantity).HasColumnType("numeric(12,2)");
        builder.Property(a => a.OldAmount).HasColumnType("numeric(18,0)");
        builder.Property(a => a.NewAmount).HasColumnType("numeric(18,0)");
        builder.Property(a => a.Difference).HasColumnType("numeric(18,0)");
        builder.Property(a => a.Handling).HasConversion<string>().HasMaxLength(16);
        builder.Property(a => a.Reason).HasMaxLength(300).IsRequired();

        builder.HasIndex(a => new { a.OrganizationId, a.InvoiceId, a.CreatedAt }).HasDatabaseName("ix_invoice_adjustments_invoice");
    }
}
