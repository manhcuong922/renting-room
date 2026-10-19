using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using renting_room.Domain.Contracts;
using renting_room.Domain.Meters;

namespace renting_room.Infrastructure.Persistence.Configurations;

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

        // MT-BR-04: mỗi công tơ 1 chỉ số lắp / tháo; mỗi (công tơ, HĐ, ngày ghi) 1 chỉ số nhận phòng / cuối — HĐ rời phòng rồi quay lại
        // có thêm chỉ số của lần ở mới; mỗi kỳ 1 chỉ số định kỳ.
        // Nhiều index cùng cột ⇒ phải đặt tên ngay khi khai báo (EF gộp index trùng cột nếu không đặt tên).
        builder.HasIndex(r => r.MeterId, "ux_meter_readings_initial").HasDatabaseName("ux_meter_readings_initial").IsUnique().HasFilter("kind = 'Initial' AND voided_at IS NULL");
        builder.HasIndex(r => r.MeterId, "ux_meter_readings_removal").HasDatabaseName("ux_meter_readings_removal").IsUnique().HasFilter("kind = 'Removal' AND voided_at IS NULL");
        builder.HasIndex(r => new { r.MeterId, r.ContractId, r.ReadingDate }, DbConstraints.HandoverReadingUnique).HasDatabaseName(DbConstraints.HandoverReadingUnique).IsUnique()
            .HasFilter("kind = 'Handover' AND voided_at IS NULL");
        builder.HasIndex(r => new { r.MeterId, r.ContractId, r.ReadingDate }, DbConstraints.FinalReadingUnique).HasDatabaseName(DbConstraints.FinalReadingUnique).IsUnique()
            .HasFilter("kind = 'Final' AND voided_at IS NULL");
        builder.HasIndex(r => new { r.MeterId, r.ContractId, r.ClosingPeriodStart }, "ux_meter_readings_periodic").HasDatabaseName("ux_meter_readings_periodic").IsUnique()
            .HasFilter("kind = 'Periodic' AND voided_at IS NULL");
        builder.HasIndex(r => new { r.MeterId, r.ReadingDate, r.Sequence }, "ix_meter_readings_order").HasDatabaseName("ix_meter_readings_order").HasFilter("voided_at IS NULL");
    }
}
