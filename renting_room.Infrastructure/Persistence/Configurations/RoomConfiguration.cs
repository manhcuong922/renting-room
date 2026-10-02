using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using renting_room.Domain.Entities;
using renting_room.Domain.Identity;

namespace renting_room.Infrastructure.Persistence.Configurations;

internal sealed class RoomConfiguration : IEntityTypeConfiguration<Room>
{
    public void Configure(EntityTypeBuilder<Room> builder)
    {
        builder.ToTable("rooms");
        builder.HasKey(r => r.Id);
        builder.ConfigureAuditable();

        builder.HasOne<Organization>()
            .WithMany()
            .HasForeignKey(r => r.OrganizationId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Property(r => r.Name).HasMaxLength(200).IsRequired();
        builder.Property(r => r.MonthlyRent).HasColumnType("numeric(18,2)").IsRequired();
        builder.Property(r => r.Status).HasConversion<string>().HasMaxLength(50).IsRequired();
    }
}
