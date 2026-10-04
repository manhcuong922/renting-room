using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using renting_room.Domain.Contracts;
using renting_room.Domain.Fees;
using renting_room.Domain.Meters;
using renting_room.Domain.Properties;

namespace renting_room.Infrastructure.Persistence.Configurations;

internal sealed class MeterConfiguration : IEntityTypeConfiguration<Meter>
{
    public void Configure(EntityTypeBuilder<Meter> builder)
    {
        builder.ToTable("meters", t =>
            t.HasCheckConstraint("ck_meters_dates", "removed_date IS NULL OR removed_date >= installed_date"));
        builder.HasKey(m => m.Id);
        builder.HasAlternateKey(m => new { m.OrganizationId, m.Id }).HasName("ak_meters_organization_id_id");
        builder.ConfigureAuditable();

        // MT-BR-02: FK gồm property_id ⇒ phòng và khoản thu bắt buộc cùng khu với công tơ.
        builder.HasOne<Room>()
            .WithMany()
            .HasForeignKey(m => new { m.OrganizationId, m.PropertyId, m.RoomId })
            .HasPrincipalKey(r => new { r.OrganizationId, r.PropertyId, r.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<FeeType>()
            .WithMany()
            .HasForeignKey(m => new { m.OrganizationId, m.PropertyId, m.FeeTypeId })
            .HasPrincipalKey(f => new { f.OrganizationId, f.PropertyId, f.Id })
            .OnDelete(DeleteBehavior.Restrict);

        builder.Property(m => m.SerialNo).HasMaxLength(50);
        builder.Property(m => m.Note).HasMaxLength(300);
        builder.Ignore(m => m.IsActive);
        builder.Ignore(m => m.Ordered);
        builder.Ignore(m => m.Latest);

        builder.HasMany(m => m.Readings).WithOne()
            .HasForeignKey(r => new { r.OrganizationId, r.MeterId })
            .HasPrincipalKey(m => new { m.OrganizationId, m.Id })
            .OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(m => m.Readings).UsePropertyAccessMode(PropertyAccessMode.Field);

        // MT-BR-01: mỗi (phòng, khoản thu) tối đa 1 công tơ đang hoạt động.
        builder.HasIndex(m => new { m.RoomId, m.FeeTypeId }).IsUnique()
            .HasFilter("removed_date IS NULL").HasDatabaseName(DbConstraints.MeterActiveUnique);
        builder.HasIndex(m => new { m.OrganizationId, m.FeeTypeId }).HasDatabaseName("ix_meters_fee_type");
    }
}

internal sealed class MeterReadingConfiguration : IEntityTypeConfiguration<MeterReading>
{
    public void Configure(EntityTypeBuilder<MeterReading> builder)
    {
        builder.ToTable("meter_readings", t =>
        {
            t.HasCheckConstraint("ck_meter_readings_value", "value >= 0");
            t.HasCheckConstraint("ck_meter_readings_contract", "kind NOT IN ('Handover','Periodic','Final') OR contract_id IS NOT NULL");
            t.HasCheckConstraint("ck_meter_readings_period", "(kind = 'Periodic') = (closing_period_start IS NOT NULL)");
        });
        builder.HasKey(r => r.Id);
        builder.ConfigureAuditable();

        builder.HasOne<Contract>()
            .WithMany()
            .HasForeignKey(r => new { r.OrganizationId, r.ContractId })
            .HasPrincipalKey(c => new { c.OrganizationId, c.Id })
            .OnDelete(DeleteBehavior.Restrict);

        builder.Property(r => r.Kind).HasConversion<string>().HasMaxLength(12);
        builder.Property(r => r.Sequence).UseIdentityAlwaysColumn();
        builder.Property(r => r.Value).HasColumnType("numeric(12,2)");
        builder.Property(r => r.Note).HasMaxLength(300);
        builder.Property(r => r.VoidReason).HasMaxLength(300);

        // MT-BR-04: mỗi công tơ 1 chỉ số lắp / tháo; mỗi (công tơ, HĐ) 1 chỉ số nhận phòng / cuối; mỗi kỳ 1 chỉ số định kỳ.
        // Nhiều index cùng cột ⇒ phải đặt tên ngay khi khai báo (EF gộp index trùng cột nếu không đặt tên).
        builder.HasIndex(r => r.MeterId, "ux_meter_readings_initial").HasDatabaseName("ux_meter_readings_initial").IsUnique().HasFilter("kind = 'Initial' AND voided_at IS NULL");
        builder.HasIndex(r => r.MeterId, "ux_meter_readings_removal").HasDatabaseName("ux_meter_readings_removal").IsUnique().HasFilter("kind = 'Removal' AND voided_at IS NULL");
        builder.HasIndex(r => new { r.MeterId, r.ContractId }, DbConstraints.HandoverReadingUnique).HasDatabaseName(DbConstraints.HandoverReadingUnique).IsUnique()
            .HasFilter("kind = 'Handover' AND voided_at IS NULL");
        builder.HasIndex(r => new { r.MeterId, r.ContractId }, "ux_meter_readings_final").HasDatabaseName("ux_meter_readings_final").IsUnique()
            .HasFilter("kind = 'Final' AND voided_at IS NULL");
        builder.HasIndex(r => new { r.MeterId, r.ContractId, r.ClosingPeriodStart }, "ux_meter_readings_periodic").HasDatabaseName("ux_meter_readings_periodic").IsUnique()
            .HasFilter("kind = 'Periodic' AND voided_at IS NULL");
        builder.HasIndex(r => new { r.MeterId, r.ReadingDate, r.Sequence }, "ix_meter_readings_order").HasDatabaseName("ix_meter_readings_order").HasFilter("voided_at IS NULL");
    }
}
