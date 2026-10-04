using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using renting_room.Domain.Fees;
using renting_room.Domain.Properties;

namespace renting_room.Infrastructure.Persistence.Configurations;

internal sealed class FeeTypeConfiguration : IEntityTypeConfiguration<FeeType>
{
    public void Configure(EntityTypeBuilder<FeeType> builder)
    {
        builder.ToTable("fee_types", t =>
        {
            // FE-BR-03 / FE-BR-02: cách tính chỉ cho nhóm Dịch vụ; số gói mặc định / loại xe chỉ cho PerUnit; mã hệ thống chỉ cho Metered.
            t.HasCheckConstraint("ck_fee_types_charge_basis", "(fee_group = 'Service') = (charge_basis IS NOT NULL)");
            t.HasCheckConstraint("ck_fee_types_default_quantity",
                "charge_basis = 'PerUnit' OR default_quantity IS NULL");
            t.HasCheckConstraint("ck_fee_types_system_code", "system_code IS NULL OR fee_group = 'Metered'");
            t.HasCheckConstraint("ck_fee_types_vehicle_type", "vehicle_type IS NULL OR charge_basis = 'PerUnit'");
        });
        builder.HasKey(f => f.Id);
        builder.HasAlternateKey(f => new { f.OrganizationId, f.Id }).HasName("ak_fee_types_organization_id_id");
        builder.HasAlternateKey(f => new { f.OrganizationId, f.PropertyId, f.Id }).HasName("ak_fee_types_organization_property_id");
        builder.ConfigureAuditable();

        builder.HasOne<Property>()
            .WithMany()
            .HasForeignKey(f => new { f.OrganizationId, f.PropertyId })
            .HasPrincipalKey(p => new { p.OrganizationId, p.Id })
            .OnDelete(DeleteBehavior.Restrict);

        builder.Property(f => f.Name).HasMaxLength(100).IsRequired();
        builder.Property(f => f.NameNormalized).HasMaxLength(100).IsRequired();
        builder.Property(f => f.Group).HasColumnName("fee_group").HasConversion<string>().HasMaxLength(16);
        builder.Property(f => f.ChargeBasis).HasConversion<string>().HasMaxLength(16);
        builder.Property(f => f.Unit).HasMaxLength(20).IsRequired();
        builder.Property(f => f.SystemCode).HasMaxLength(20);
        builder.Property(f => f.DefaultQuantity).HasColumnType("numeric(12,2)");
        builder.Property(f => f.VehicleType).HasConversion<string>().HasMaxLength(16);
        builder.Ignore(f => f.IsArchived);
        builder.Ignore(f => f.AttachQuantity);

        builder.HasMany(f => f.Prices).WithOne()
            .HasForeignKey(p => new { p.OrganizationId, p.FeeTypeId })
            .HasPrincipalKey(f => new { f.OrganizationId, f.Id })
            .OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(f => f.Prices).UsePropertyAccessMode(PropertyAccessMode.Field);

        // FE-BR-01 / FE-BR-04: tên (không phân biệt hoa thường) và mã hệ thống duy nhất trong khu, tính trên khoản chưa ngừng dùng.
        builder.HasIndex(f => new { f.PropertyId, f.NameNormalized }).IsUnique()
            .HasFilter("archived_at IS NULL").HasDatabaseName(DbConstraints.FeeNameUnique);
        builder.HasIndex(f => new { f.PropertyId, f.SystemCode }).IsUnique()
            .HasFilter("archived_at IS NULL AND system_code IS NOT NULL").HasDatabaseName(DbConstraints.FeeSystemCodeUnique);
    }
}

internal sealed class FeePriceConfiguration : IEntityTypeConfiguration<FeePrice>
{
    public void Configure(EntityTypeBuilder<FeePrice> builder)
    {
        builder.ToTable("fee_prices", t => t.HasCheckConstraint("ck_fee_prices_unit_price", "unit_price >= 0"));
        builder.HasKey(p => p.Id);
        builder.ConfigureAuditable();

        builder.Property(p => p.UnitPrice).HasColumnType("numeric(18,2)");
        builder.Property(p => p.Note).HasMaxLength(300);

        builder.HasIndex(p => new { p.FeeTypeId, p.EffectiveFrom }).IsUnique().HasDatabaseName(DbConstraints.FeePriceDateUnique);
    }
}
