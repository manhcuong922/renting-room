using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using renting_room.Domain.Identity;

namespace renting_room.Infrastructure.Persistence.Configurations;

internal sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.ToTable("users", t =>
        {
            t.HasCheckConstraint("ck_users_has_username", "phone_normalized IS NOT NULL OR email_normalized IS NOT NULL");
            t.HasCheckConstraint("ck_users_role_organization", "(role = 'SystemAdmin') = (organization_id IS NULL)");
            t.HasCheckConstraint("ck_users_failed_login_count", "failed_login_count >= 0");
            t.HasCheckConstraint("ck_users_removed", "(status = 'Removed') = (removed_at IS NOT NULL)");
        });

        builder.HasKey(u => u.Id);
        builder.ConfigureAuditable();
        builder.Ignore(u => u.Username);

        builder.Property(u => u.Role).HasConversion<string>().HasMaxLength(16).IsRequired();
        builder.Property(u => u.Status).HasConversion<string>().HasMaxLength(16).IsRequired();
        builder.Property(u => u.FullName).HasMaxLength(200).IsRequired();
        builder.Property(u => u.PhoneNormalized).HasMaxLength(15);
        builder.Property(u => u.EmailNormalized).HasMaxLength(254);
        builder.Property(u => u.PasswordHash).HasMaxLength(256).IsRequired();
        builder.Property(u => u.SecurityStamp).IsRequired();

        builder.HasOne<Organization>()
            .WithMany()
            .HasForeignKey(u => u.OrganizationId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(u => u.PhoneNormalized)
            .IsUnique()
            .HasFilter("phone_normalized IS NOT NULL")
            .HasDatabaseName(DbConstraints.UserPhoneUnique);

        builder.HasIndex(u => u.EmailNormalized)
            .IsUnique()
            .HasFilter("email_normalized IS NOT NULL")
            .HasDatabaseName(DbConstraints.UserEmailUnique);

        // Index đầy đủ cho FK — index một phần bên dưới chỉ chứa dòng OrgOwner nên không thay thế được.
        builder.HasIndex(u => u.OrganizationId).HasDatabaseName("ix_users_organization_id");

        builder.HasIndex(u => new { u.OrganizationId, u.Role })
            .IsUnique()
            .HasFilter("role = 'OrgOwner'")
            .HasDatabaseName(DbConstraints.OrganizationOwnerUnique);
    }
}
