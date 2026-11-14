using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using renting_room.Domain.Identity;
using renting_room.Domain.Properties;

namespace renting_room.Infrastructure.Persistence.Configurations;

/// <summary>M01 ID-BR-24 (08/11/2026): khu được giao cho quản lý khu.</summary>
internal sealed class UserPropertyAccessConfiguration : IEntityTypeConfiguration<UserPropertyAccess>
{
    public void Configure(EntityTypeBuilder<UserPropertyAccess> builder)
    {
        builder.ToTable("user_property_access");
        builder.HasKey(a => a.Id);
        builder.ConfigureAuditable();

        builder.HasOne<User>().WithMany().HasForeignKey(a => a.UserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Property>()
            .WithMany()
            .HasForeignKey(a => new { a.OrganizationId, a.PropertyId })
            .HasPrincipalKey(p => new { p.OrganizationId, p.Id })
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(a => new { a.UserId, a.PropertyId }).IsUnique().HasDatabaseName("ux_user_property_access_user_property");
        builder.HasIndex(a => new { a.OrganizationId, a.PropertyId }).HasDatabaseName("ix_user_property_access_property");
    }
}
