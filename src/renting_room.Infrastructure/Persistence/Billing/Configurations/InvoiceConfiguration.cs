using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using renting_room.Domain.Billing;
using renting_room.Domain.Contracts;
using renting_room.Domain.Payments;
using renting_room.Domain.Properties;

namespace renting_room.Infrastructure.Persistence.Configurations;

internal sealed class InvoiceConfiguration : IEntityTypeConfiguration<Invoice>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public void Configure(EntityTypeBuilder<Invoice> builder)
    {
        builder.ToTable("invoices", t =>
        {
            t.HasCheckConstraint("ck_invoices_period", "period_end >= period_start");
            t.HasCheckConstraint("ck_invoices_total", "subtotal + discount_total >= 0 OR status = 'Draft' OR invoice_type = 'Final'");
            t.HasCheckConstraint("ck_invoices_refund",
                "refund_total <= 0 AND total_amount = subtotal + discount_total + refund_total + rounding_amount");
            t.HasCheckConstraint("ck_invoices_rounding", "rounding_amount > -1000 AND rounding_amount < 1000");
            t.HasCheckConstraint("ck_invoices_refunded", "refunded_on IS NULL OR (total_amount < 0 AND refund_method IS NOT NULL)");
            t.HasCheckConstraint("ck_invoices_paid", "paid_amount >= 0 AND paid_amount <= GREATEST(total_amount, 0)");
            t.HasCheckConstraint("ck_invoices_written_off", "written_off_amount >= 0 AND written_off_amount <= paid_amount");
            t.HasCheckConstraint("ck_invoices_number", "status = 'Draft' OR (invoice_no IS NOT NULL AND issue_date IS NOT NULL AND due_date IS NOT NULL)");
            t.HasCheckConstraint("ck_invoices_void_reason", "status <> 'Void' OR void_reason IS NOT NULL");
        });
        builder.HasKey(i => i.Id);
        builder.HasAlternateKey(i => new { i.OrganizationId, i.Id }).HasName("ak_invoices_organization_id_id");
        builder.HasAlternateKey(i => new { i.OrganizationId, i.ContractId, i.Id }).HasName("ak_invoices_organization_contract_id");
        builder.ConfigureAuditable();

        builder.HasOne<Contract>()
            .WithMany()
            .HasForeignKey(i => new { i.OrganizationId, i.PropertyId, i.ContractId })
            .HasPrincipalKey(c => new { c.OrganizationId, c.PropertyId, c.Id })
            .OnDelete(DeleteBehavior.Restrict);

        builder.Property(i => i.Type).HasColumnName("invoice_type").HasConversion<string>().HasMaxLength(8);
        builder.Property(i => i.Status).HasConversion<string>().HasMaxLength(12);
        builder.Property(i => i.InvoiceNo).HasMaxLength(20);
        foreach (var money in new[] { nameof(Invoice.Subtotal), nameof(Invoice.DiscountTotal), nameof(Invoice.TotalAmount), nameof(Invoice.PaidAmount), nameof(Invoice.WrittenOffAmount), nameof(Invoice.RefundTotal),
                     nameof(Invoice.RoundingAmount) })
            builder.Property(money).HasColumnType("numeric(18,0)");
        builder.Property(i => i.SnapshotRoomCode).HasMaxLength(20).IsRequired();
        builder.Property(i => i.SnapshotContractNo).HasMaxLength(30).IsRequired();
        builder.Property(i => i.SnapshotRepresentativeName).HasMaxLength(200).IsRequired();
        builder.Property(i => i.Note).HasMaxLength(1000);
        builder.Property(i => i.VoidReason).HasMaxLength(300);
        builder.HasIndex(i => new { i.OrganizationId, i.PropertyId }, "ix_invoices_needs_resend").HasDatabaseName("ix_invoices_needs_resend")
            .HasFilter("needs_resend");
        builder.HasIndex(i => new { i.OrganizationId, i.PropertyId }, "ix_invoices_stale_drafts").HasDatabaseName("ix_invoices_stale_drafts")
            .HasFilter("status = 'Draft' AND is_stale");
        builder.Property(i => i.RefundMethod).HasConversion<string>().HasMaxLength(16);
        builder.Property(i => i.RefundNote).HasMaxLength(300);
        builder.Property(i => i.Issues)
            .HasColumnType("jsonb")
            .HasConversion(
                v => JsonSerializer.Serialize(v, Json),
                v => JsonSerializer.Deserialize<List<InvoiceIssue>>(v, Json) ?? new List<InvoiceIssue>(),
                new ValueComparer<List<InvoiceIssue>>(
                    (a, b) => a!.SequenceEqual(b!),
                    v => v.Aggregate(0, (hash, i) => HashCode.Combine(hash, i)),
                    v => v.ToList()));
        builder.Ignore(i => i.Outstanding);
        builder.Ignore(i => i.RefundDue);
        builder.Ignore(i => i.NetCharges);
        builder.Ignore(i => i.HasBlockingIssues);

        builder.HasMany(i => i.Lines).WithOne()
            .HasForeignKey(l => new { l.OrganizationId, l.InvoiceId }).HasPrincipalKey(i => new { i.OrganizationId, i.Id })
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(i => i.Segments).WithOne()
            .HasForeignKey(s => new { s.OrganizationId, s.InvoiceId }).HasPrincipalKey(i => new { i.OrganizationId, i.Id })
            .OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(i => i.Lines).UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.Navigation(i => i.Segments).UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasIndex(i => new { i.ContractId, i.Type, i.PeriodStart }, DbConstraints.InvoicePeriodUnique)
            .HasDatabaseName(DbConstraints.InvoicePeriodUnique).IsUnique().HasFilter("status <> 'Void'");
        builder.HasIndex(i => new { i.OrganizationId, i.InvoiceNo }, "ux_invoices_invoice_no")
            .HasDatabaseName("ux_invoices_invoice_no").IsUnique().HasFilter("invoice_no IS NOT NULL");
        builder.HasIndex(i => new { i.OrganizationId, i.PropertyId, i.BillingMonth }, "ix_invoices_property_month")
            .HasDatabaseName("ix_invoices_property_month");
        builder.HasIndex(i => new { i.OrganizationId, i.RoomId, i.PeriodStart }, "ix_invoices_room").HasDatabaseName("ix_invoices_room");
    }
}
