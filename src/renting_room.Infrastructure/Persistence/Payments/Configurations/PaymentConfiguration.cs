using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using renting_room.Domain.Contracts;
using renting_room.Domain.Payments;

namespace renting_room.Infrastructure.Persistence.Configurations;

internal sealed class PaymentConfiguration : IEntityTypeConfiguration<Payment>
{
    public void Configure(EntityTypeBuilder<Payment> builder)
    {
        builder.ToTable("payments", t =>
        {
            t.HasCheckConstraint("ck_payments_amount", "amount > 0");
            t.HasCheckConstraint("ck_payments_reverse_reason", "status <> 'Reversed' OR reverse_reason IS NOT NULL");
        });
        builder.HasKey(p => p.Id);
        builder.HasAlternateKey(p => new { p.OrganizationId, p.ContractId, p.Id }).HasName("ak_payments_organization_contract_id");
        builder.ConfigureAuditable();

        builder.HasOne<Contract>()
            .WithMany()
            .HasForeignKey(p => new { p.OrganizationId, p.PropertyId, p.ContractId })
            .HasPrincipalKey(c => new { c.OrganizationId, c.PropertyId, c.Id })
            .OnDelete(DeleteBehavior.Restrict);

        builder.Property(p => p.ReceiptNo).HasMaxLength(20).IsRequired();
        builder.Property(p => p.Amount).HasColumnType("numeric(18,0)");
        builder.Property(p => p.Method).HasConversion<string>().HasMaxLength(20);
        builder.Property(p => p.Status).HasConversion<string>().HasMaxLength(10);
        builder.Property(p => p.Kind).HasConversion<string>().HasMaxLength(10);
        builder.Property(p => p.PayerName).HasMaxLength(200);
        builder.Property(p => p.Reference).HasMaxLength(100);
        builder.Property(p => p.Note).HasMaxLength(500);
        builder.Property(p => p.ReverseReason).HasMaxLength(500);

        builder.HasMany(p => p.Allocations).WithOne()
            .HasForeignKey(a => new { a.OrganizationId, a.ContractId, a.PaymentId })
            .HasPrincipalKey(p => new { p.OrganizationId, p.ContractId, p.Id })
            .OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(p => p.Allocations).UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasIndex(p => new { p.OrganizationId, p.ReceiptNo }, "ux_payments_receipt_no").HasDatabaseName("ux_payments_receipt_no").IsUnique();
        builder.HasIndex(p => new { p.OrganizationId, p.ContractId, p.PaidAt }, "ix_payments_contract").HasDatabaseName("ix_payments_contract");
    }
}
