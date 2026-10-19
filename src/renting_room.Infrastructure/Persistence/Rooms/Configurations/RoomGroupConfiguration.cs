using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using renting_room.Domain.Identity;
using renting_room.Domain.Properties;

namespace renting_room.Infrastructure.Persistence.Configurations;

internal sealed class RoomGroupConfiguration : IEntityTypeConfiguration<RoomGroup>
{
    public void Configure(EntityTypeBuilder<RoomGroup> builder)
    {
        builder.ToTable("room_groups");
        builder.HasKey(g => g.Id);
        builder.HasAlternateKey(g => new { g.OrganizationId, g.PropertyId, g.Id }).HasName("ak_room_groups_organization_property_id");
        builder.ConfigureAuditable();

        builder.HasOne<Property>()
            .WithMany()
            .HasForeignKey(g => new { g.OrganizationId, g.PropertyId })
            .HasPrincipalKey(p => new { p.OrganizationId, p.Id })
            .OnDelete(DeleteBehavior.Restrict);

        builder.Property(g => g.Name).HasMaxLength(100).IsRequired();
        builder.Property(g => g.Description).HasMaxLength(500);

        builder.HasMany(g => g.Members)
            .WithOne()
            .HasForeignKey(m => new { m.OrganizationId, m.PropertyId, m.RoomGroupId })
            .HasPrincipalKey(g => new { g.OrganizationId, g.PropertyId, g.Id })
            .OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(g => g.Members).UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasIndex(g => new { g.PropertyId, g.Name }).HasDatabaseName("ix_room_groups_property_name");
    }
}
