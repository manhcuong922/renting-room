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
            // ID-BR-01: phải có ít nhất SĐT hoặc email để đăng nhập.
            t.HasCheckConstraint("ck_users_has_username", "phone_normalized IS NOT NULL OR email_normalized IS NOT NULL");
            // ID-BR-03: SystemAdmin ⇔ không thuộc tổ chức.
            t.HasCheckConstraint("ck_users_role_organization", "(role = 'SystemAdmin') = (organization_id IS NULL)");
            t.HasCheckConstraint("ck_users_failed_login_count", "failed_login_count >= 0");
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

        // ID-BR-02: unique toàn hệ thống vì dùng để đăng nhập.
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

        // ID-BR-04: mỗi tổ chức đúng một OrgOwner.
        builder.HasIndex(u => new { u.OrganizationId, u.Role })
            .IsUnique()
            .HasFilter("role = 'OrgOwner'")
            .HasDatabaseName(DbConstraints.OrganizationOwnerUnique);
    }
}
