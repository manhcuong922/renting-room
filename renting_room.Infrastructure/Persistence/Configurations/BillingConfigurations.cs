using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using renting_room.Domain.Billing;
using renting_room.Domain.Contracts;
using renting_room.Domain.Meters;
using renting_room.Domain.Payments;

namespace renting_room.Infrastructure.Persistence.Configurations;

internal sealed class InvoiceConfiguration : IEntityTypeConfiguration<Invoice>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public void Configure(EntityTypeBuilder<Invoice> builder)
    {
        builder.ToTable("invoices", t =>
        {
            t.HasCheckConstraint("ck_invoices_period", "period_end >= period_start");
            // BL-BR-10: giảm trừ không vượt phần thu; chỉ dòng hoàn trả được làm tổng âm (BL-BR-27).
            t.HasCheckConstraint("ck_invoices_total", "subtotal + discount_total >= 0 OR status = 'Draft'");
            t.HasCheckConstraint("ck_invoices_refund", "refund_total <= 0 AND total_amount = subtotal + discount_total + refund_total");
            t.HasCheckConstraint("ck_invoices_refunded", "refunded_on IS NULL OR (total_amount < 0 AND refund_method IS NOT NULL)");
            t.HasCheckConstraint("ck_invoices_paid", "paid_amount >= 0 AND paid_amount <= GREATEST(total_amount, 0)");
            t.HasCheckConstraint("ck_invoices_written_off", "written_off_amount >= 0 AND written_off_amount <= paid_amount");
            t.HasCheckConstraint("ck_invoices_number", "status = 'Draft' OR (invoice_no IS NOT NULL AND issue_date IS NOT NULL AND due_date IS NOT NULL)");
            t.HasCheckConstraint("ck_invoices_void_reason", "status <> 'Void' OR void_reason IS NOT NULL");
        });
        builder.HasKey(i => i.Id);
        builder.HasAlternateKey(i => new { i.OrganizationId, i.Id }).HasName("ak_invoices_organization_id_id");
        // Đích FK của phân bổ thanh toán: (org, contract, invoice) ⇒ DB chặn phân bổ chéo hợp đồng (M08).
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
        foreach (var money in new[] { nameof(Invoice.Subtotal), nameof(Invoice.DiscountTotal), nameof(Invoice.TotalAmount), nameof(Invoice.PaidAmount), nameof(Invoice.WrittenOffAmount), nameof(Invoice.RefundTotal) })
            builder.Property(money).HasColumnType("numeric(18,0)");
        builder.Property(i => i.SnapshotRoomCode).HasMaxLength(20).IsRequired();
        builder.Property(i => i.SnapshotContractNo).HasMaxLength(30).IsRequired();
        builder.Property(i => i.SnapshotRepresentativeName).HasMaxLength(200).IsRequired();
        builder.Property(i => i.Note).HasMaxLength(1000);
        builder.Property(i => i.VoidReason).HasMaxLength(300);
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

        // BL-BR-01: mỗi (HĐ, loại, kỳ) tối đa 1 phiếu chưa hủy; số phiếu duy nhất trong tổ chức.
        builder.HasIndex(i => new { i.ContractId, i.Type, i.PeriodStart }, DbConstraints.InvoicePeriodUnique)
            .HasDatabaseName(DbConstraints.InvoicePeriodUnique).IsUnique().HasFilter("status <> 'Void'");
        builder.HasIndex(i => new { i.OrganizationId, i.InvoiceNo }, "ux_invoices_invoice_no")
            .HasDatabaseName("ux_invoices_invoice_no").IsUnique().HasFilter("invoice_no IS NOT NULL");
        builder.HasIndex(i => new { i.OrganizationId, i.PropertyId, i.BillingMonth }, "ix_invoices_property_month")
            .HasDatabaseName("ix_invoices_property_month");
        builder.HasIndex(i => new { i.OrganizationId, i.RoomId, i.PeriodStart }, "ix_invoices_room").HasDatabaseName("ix_invoices_room");
    }
}

internal sealed class InvoiceLineConfiguration : IEntityTypeConfiguration<InvoiceLine>
{
    public void Configure(EntityTypeBuilder<InvoiceLine> builder)
    {
        builder.ToTable("invoice_lines", t =>
        {
            t.HasCheckConstraint("ck_invoice_lines_sign", "line_type IN ('ManualDiscount','Refund') OR amount >= 0");
            t.HasCheckConstraint("ck_invoice_lines_discount", "line_type NOT IN ('ManualDiscount','Refund') OR amount <= 0");
            t.HasCheckConstraint("ck_invoice_lines_note", "line_type NOT IN ('Surcharge','ManualDiscount','Refund') OR note IS NOT NULL");
        });
        builder.HasKey(l => l.Id);
        builder.ConfigureAuditable();

        builder.Property(l => l.Type).HasColumnName("line_type").HasConversion<string>().HasMaxLength(16);
        builder.Property(l => l.Description).HasMaxLength(200).IsRequired();
        builder.Property(l => l.Unit).HasMaxLength(20);
        builder.Property(l => l.Quantity).HasColumnType("numeric(12,2)");
        builder.Property(l => l.UnitPrice).HasColumnType("numeric(18,2)");
        builder.Property(l => l.ProrationFactor).HasColumnType("numeric(12,8)");
        builder.Property(l => l.Amount).HasColumnType("numeric(18,0)");
        builder.Property(l => l.SystemQuantity).HasColumnType("numeric(12,2)");
        builder.Property(l => l.SystemUnitPrice).HasColumnType("numeric(18,2)");
        builder.Property(l => l.SystemAmount).HasColumnType("numeric(18,0)");
        builder.Property(l => l.Note).HasMaxLength(300);
        builder.Ignore(l => l.Key);

        builder.HasIndex(l => l.InvoiceId, "ux_invoice_lines_rent").HasDatabaseName("ux_invoice_lines_rent")
            .IsUnique().HasFilter("line_type = 'Rent' AND is_system");
        builder.HasIndex(l => new { l.InvoiceId, l.FeeTypeId }, "ux_invoice_lines_fee").HasDatabaseName("ux_invoice_lines_fee")
            .IsUnique().HasFilter("fee_type_id IS NOT NULL AND is_system");
        builder.HasIndex(l => new { l.OrganizationId, l.FeeTypeId }, "ix_invoice_lines_fee_type").HasDatabaseName("ix_invoice_lines_fee_type");
    }
}

internal sealed class InvoiceMeterSegmentConfiguration : IEntityTypeConfiguration<InvoiceMeterSegment>
{
    public void Configure(EntityTypeBuilder<InvoiceMeterSegment> builder)
    {
        builder.ToTable("invoice_meter_segments", t =>
            t.HasCheckConstraint("ck_invoice_meter_segments_values", "end_value >= start_value AND consumption = end_value - start_value"));
        builder.HasKey(s => s.Id);
        builder.ConfigureAuditable();

        builder.HasOne<Meter>()
            .WithMany()
            .HasForeignKey(s => new { s.OrganizationId, s.MeterId })
            .HasPrincipalKey(m => new { m.OrganizationId, m.Id })
            .OnDelete(DeleteBehavior.Restrict);

        foreach (var value in new[] { nameof(InvoiceMeterSegment.StartValue), nameof(InvoiceMeterSegment.EndValue), nameof(InvoiceMeterSegment.Consumption) })
            builder.Property(value).HasColumnType("numeric(12,2)");

        // Một khoảng chỉ số không thể bị tính 2 lần (M06 §10).
        builder.HasIndex(s => s.EndReadingId, DbConstraints.SegmentEndReadingUnique).HasDatabaseName(DbConstraints.SegmentEndReadingUnique)
            .IsUnique().HasFilter("NOT voided");
        builder.HasIndex(s => s.StartReadingId, "ux_invoice_meter_segments_start").HasDatabaseName("ux_invoice_meter_segments_start")
            .IsUnique().HasFilter("NOT voided");
    }
}

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
