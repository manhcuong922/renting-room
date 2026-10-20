using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using renting_room.Domain.Contracts;
using renting_room.Domain.Properties;
using renting_room.Domain.Renters;

namespace renting_room.Infrastructure.Persistence.Configurations;

internal sealed class ContractVehicleConfiguration : IEntityTypeConfiguration<ContractVehicle>
{
    public void Configure(EntityTypeBuilder<ContractVehicle> builder)
    {
        builder.ToTable("contract_vehicles", t =>
            t.HasCheckConstraint("ck_contract_vehicles_dates", "registered_to IS NULL OR registered_to >= registered_from"));
        builder.HasKey(v => v.Id);
        builder.ConfigureAuditable();
        builder.Ignore(v => v.IsActive);
        builder.Property(v => v.VehicleType).HasConversion<string>().HasMaxLength(16);
        builder.Property(v => v.PlateNumber).HasMaxLength(20);
        builder.Property(v => v.BrandColor).HasMaxLength(100);
        builder.Property(v => v.Note).HasMaxLength(500);

        builder.HasIndex(v => new { v.OrganizationId, v.PlateNumber })
            .IsUnique()
            .HasFilter("registered_to IS NULL AND plate_number IS NOT NULL")
            .HasDatabaseName(DbConstraints.ActivePlateUnique);
    }
}
