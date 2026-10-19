using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using renting_room.Domain.Billing;
using renting_room.Domain.Meters;
using renting_room.Domain.Payments;

namespace renting_room.Infrastructure.Persistence.Configurations;

internal sealed class PaymentAllocationConfiguration : IEntityTypeConfiguration<PaymentAllocation>
{
    public void Configure(EntityTypeBuilder<PaymentAllocation> builder)
    {
        builder.ToTable("payment_allocations", t => t.HasCheckConstraint("ck_payment_allocations_amount", "amount > 0"));
        builder.HasKey(a => a.Id);
        builder.ConfigureAuditable();

        // PM-BR-02: phiếu báo phải cùng HĐ với phiếu thu — FK gồm contract_id.
        builder.HasOne<Invoice>()
            .WithMany()
            .HasForeignKey(a => new { a.OrganizationId, a.ContractId, a.InvoiceId })
            .HasPrincipalKey(i => new { i.OrganizationId, i.ContractId, i.Id })
            .OnDelete(DeleteBehavior.Restrict);

        builder.Property(a => a.Amount).HasColumnType("numeric(18,0)");
        builder.HasIndex(a => new { a.OrganizationId, a.InvoiceId }, "ix_payment_allocations_invoice").HasDatabaseName("ix_payment_allocations_invoice");
    }
}
