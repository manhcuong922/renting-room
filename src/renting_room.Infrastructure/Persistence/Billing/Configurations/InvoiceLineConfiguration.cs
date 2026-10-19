using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using renting_room.Domain.Billing;
using renting_room.Domain.Meters;
using renting_room.Domain.Payments;
using renting_room.Domain.Properties;

namespace renting_room.Infrastructure.Persistence.Configurations;

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
        // BL-BR-33: dòng của khoản phát sinh (không unique — phiếu đã hủy vẫn giữ dòng cũ khi khoản vào phiếu lập lại).
        builder.HasIndex(l => l.RoomChargeId, "ix_invoice_lines_room_charge").HasDatabaseName("ix_invoice_lines_room_charge")
            .HasFilter("room_charge_id IS NOT NULL");
        // BL-BR-27: cộng các lần hoàn của 1 phiếu nguồn.
        builder.HasIndex(l => l.SourceInvoiceId, "ix_invoice_lines_source_invoice").HasDatabaseName("ix_invoice_lines_source_invoice")
            .HasFilter("source_invoice_id IS NOT NULL");

        builder.HasIndex(l => l.InvoiceId, "ux_invoice_lines_rent").HasDatabaseName("ux_invoice_lines_rent")
            .IsUnique().HasFilter("line_type = 'Rent' AND is_system");
        // Mỗi khoản 1 dòng hệ thống cho mỗi khoảng dịch vụ — phiếu quyết toán khu thu trước có 2 dòng điện nước cùng khoản (BL-BR-05).
        builder.HasIndex(l => new { l.InvoiceId, l.FeeTypeId, l.ServiceFrom }, "ux_invoice_lines_fee").HasDatabaseName("ux_invoice_lines_fee")
            .IsUnique().HasFilter("fee_type_id IS NOT NULL AND is_system");
        builder.HasIndex(l => new { l.OrganizationId, l.FeeTypeId }, "ix_invoice_lines_fee_type").HasDatabaseName("ix_invoice_lines_fee_type");
    }
}
