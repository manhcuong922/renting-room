using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using renting_room.Domain.Identity;
using renting_room.Domain.Properties;

namespace renting_room.Infrastructure.Persistence.Configurations;

internal sealed class RoomConfiguration : IEntityTypeConfiguration<Room>
{
    public void Configure(EntityTypeBuilder<Room> builder)
    {
        builder.ToTable("rooms", t =>
        {
            t.HasCheckConstraint("ck_rooms_max_occupants", $"max_occupants IS NULL OR max_occupants BETWEEN 1 AND {Room.MaxOccupantsLimit}");
            t.HasCheckConstraint("ck_rooms_area", "area_m2 IS NULL OR area_m2 > 0");
            t.HasCheckConstraint("ck_rooms_money", "(listed_rent IS NULL OR listed_rent >= 0) AND (default_deposit IS NULL OR default_deposit >= 0)");
            t.HasCheckConstraint("ck_rooms_maintenance_not_archived", "NOT (is_under_maintenance AND archived_at IS NOT NULL)");
        });

        builder.HasKey(r => r.Id);
        // Hợp đồng, thành viên nhóm trỏ tới (organization_id, property_id, id) ⇒ phòng phải cùng tổ chức VÀ cùng khu.
        builder.HasAlternateKey(r => new { r.OrganizationId, r.PropertyId, r.Id }).HasName("ak_rooms_organization_property_id");
        builder.ConfigureAuditable();

        builder.HasOne<Property>()
            .WithMany()
            .HasForeignKey(r => new { r.OrganizationId, r.PropertyId })
            .HasPrincipalKey(p => new { p.OrganizationId, p.Id })
            .OnDelete(DeleteBehavior.Restrict);

        builder.Property(r => r.Code).HasMaxLength(20).IsRequired();
        builder.Property(r => r.Floor).HasMaxLength(10);
        builder.Property(r => r.AreaM2).HasColumnType("numeric(6,2)");
        builder.Property(r => r.ListedRent).HasColumnType("numeric(18,0)");
        builder.Property(r => r.DefaultDeposit).HasColumnType("numeric(18,0)");
        builder.Property(r => r.Amenities).HasColumnType("text[]");
        builder.Property(r => r.Description).HasMaxLength(2000);
        builder.Property(r => r.MaintenanceNote).HasMaxLength(500);

        builder.HasIndex(r => new { r.PropertyId, r.Code }).IsUnique().HasDatabaseName(DbConstraints.RoomCodeUnique);
    }
}
