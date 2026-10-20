using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using renting_room.Domain.Identity;
using renting_room.Domain.Properties;

namespace renting_room.Infrastructure.Persistence.Configurations;

internal sealed class RoomGroupMemberConfiguration : IEntityTypeConfiguration<RoomGroupMember>
{
    public void Configure(EntityTypeBuilder<RoomGroupMember> builder)
    {
        builder.ToTable("room_group_members");
        builder.HasKey(m => m.Id);
        builder.ConfigureAuditable();

        builder.HasOne<Room>()
            .WithMany()
            .HasForeignKey(m => new { m.OrganizationId, m.PropertyId, m.RoomId })
            .HasPrincipalKey(r => new { r.OrganizationId, r.PropertyId, r.Id })
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(m => new { m.RoomGroupId, m.RoomId }).IsUnique().HasDatabaseName("ux_room_group_members_group_room");
    }
}
