using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using renting_room.Domain.Billing;
using renting_room.Domain.Contracts;
using renting_room.Domain.Payments;
using renting_room.Domain.Properties;

namespace renting_room.Infrastructure.Persistence.Configurations;

internal sealed class RoomChargeConfiguration : IEntityTypeConfiguration<RoomCharge>
{
    public void Configure(EntityTypeBuilder<RoomCharge> builder)
    {
        builder.ToTable("room_charges", t =>
        {
            t.HasCheckConstraint("ck_room_charges_amount", "amount > 0");
            t.HasCheckConstraint("ck_room_charges_settled", "NOT is_settled OR (settled_on IS NOT NULL AND settled_method IS NOT NULL)");
            t.HasCheckConstraint("ck_room_charges_cancelled", "cancelled_at IS NULL OR (cancel_reason IS NOT NULL AND invoice_id IS NULL)");
            t.HasCheckConstraint("ck_room_charges_meter_closing",
                "meter_id IS NULL OR (start_reading_id IS NOT NULL AND end_reading_id IS NOT NULL AND fee_type_id IS NOT NULL "
                + "AND quantity IS NOT NULL AND unit_price IS NOT NULL)");
            t.HasCheckConstraint("ck_room_charges_reading_adjustment",
                "source_invoice_id IS NULL OR (fee_type_id IS NOT NULL AND source_usage_start IS NOT NULL)");
        });
        builder.HasKey(c => c.Id);
        builder.ConfigureAuditable();
        builder.Ignore(c => c.IsCancelled);
        builder.Ignore(c => c.IsPending);
        builder.Ignore(c => c.IsSystem);
        builder.Ignore(c => c.IsReadingAdjustment);
        builder.Ignore(c => c.LineType);

        builder.HasOne<Contract>()
            .WithMany()
            .HasForeignKey(c => new { c.OrganizationId, c.PropertyId, c.ContractId })
            .HasPrincipalKey(c => new { c.OrganizationId, c.PropertyId, c.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Room>().WithMany().HasForeignKey(c => c.RoomId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Invoice>().WithMany().HasForeignKey(c => c.InvoiceId).OnDelete(DeleteBehavior.SetNull);

        builder.Property(c => c.Kind).HasConversion<string>().HasMaxLength(10);
        builder.Property(c => c.Description).HasMaxLength(200).IsRequired();
        builder.Property(c => c.Reason).HasMaxLength(300).IsRequired();
        builder.Property(c => c.Amount).HasColumnType("numeric(18,0)");
        builder.Property(c => c.SettledMethod).HasConversion<string>().HasMaxLength(20);
        builder.Property(c => c.CancelReason).HasMaxLength(300);
        builder.Property(c => c.Quantity).HasColumnType("numeric(12,2)");
        builder.Property(c => c.UnitPrice).HasColumnType("numeric(18,2)");

        builder.HasIndex(c => new { c.OrganizationId, c.RoomId, c.IncurredOn }).HasDatabaseName("ix_room_charges_room");
        builder.HasIndex(c => new { c.SourceInvoiceId, c.FeeTypeId, c.SourceUsageStart }).HasDatabaseName(DbConstraints.ReadingAdjustmentUnique)
            .IsUnique().HasFilter("source_invoice_id IS NOT NULL AND cancelled_at IS NULL");
        // MT-BR-06: tra chỉ số đang bị khoản chốt khóa theo công tơ.
        builder.HasIndex(c => c.MeterId).HasDatabaseName("ix_room_charges_meter_closing").HasFilter("meter_id IS NOT NULL AND cancelled_at IS NULL");
        builder.HasIndex(c => new { c.OrganizationId, c.ContractId }).HasDatabaseName("ix_room_charges_pending")
            .HasFilter("invoice_id IS NULL AND cancelled_at IS NULL");
    }
}
