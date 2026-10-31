using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using renting_room.Domain.Billing;
using renting_room.Domain.Contracts;

namespace renting_room.Infrastructure.Persistence.Configurations;

internal sealed class ContractBillingGapConfiguration : IEntityTypeConfiguration<ContractBillingGap>
{
    public void Configure(EntityTypeBuilder<ContractBillingGap> builder)
    {
        builder.ToTable("contract_billing_gaps", t => t.HasCheckConstraint(
            "ck_contract_billing_gaps_range", "from_date <= to_date AND period_count BETWEEN 1 AND 12"));
        builder.HasKey(g => g.Id);
        builder.ConfigureAuditable();

        builder.HasOne<Contract>().WithMany()
            .HasForeignKey(g => new { g.OrganizationId, g.ContractId }).HasPrincipalKey(c => new { c.OrganizationId, c.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Invoice>().WithMany()
            .HasForeignKey(g => new { g.OrganizationId, g.ResumedInvoiceId }).HasPrincipalKey(i => new { i.OrganizationId, i.Id })
            .OnDelete(DeleteBehavior.Restrict);

        builder.Property(g => g.Note).HasMaxLength(500);

        builder.HasIndex(g => g.ResumedInvoiceId).IsUnique().HasFilter("resumed_invoice_id IS NOT NULL").HasDatabaseName("ux_contract_billing_gaps_invoice");
        builder.HasIndex(g => new { g.OrganizationId, g.ContractId, g.FromDate }).HasDatabaseName("ix_contract_billing_gaps_contract");
    }
}
