using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using renting_room.Domain.Billing;
using renting_room.Domain.Meters;
using renting_room.Domain.Properties;

namespace renting_room.Infrastructure.Persistence.Configurations;

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
        builder.Property(s => s.RoomCode).HasMaxLength(20);
        builder.Property(s => s.MeterSerial).HasMaxLength(50);

        // Một khoảng chỉ số không thể bị tính 2 lần (M06 §10).
        builder.HasIndex(s => s.EndReadingId, DbConstraints.SegmentEndReadingUnique).HasDatabaseName(DbConstraints.SegmentEndReadingUnique)
            .IsUnique().HasFilter("NOT voided");
        builder.HasIndex(s => s.StartReadingId, "ux_invoice_meter_segments_start").HasDatabaseName("ux_invoice_meter_segments_start")
            .IsUnique().HasFilter("NOT voided");
    }
}
