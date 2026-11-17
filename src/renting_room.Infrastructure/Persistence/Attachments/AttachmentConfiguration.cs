using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using renting_room.Domain.Attachments;
using renting_room.Domain.Identity;

namespace renting_room.Infrastructure.Persistence.Configurations;

internal sealed class AttachmentConfiguration : IEntityTypeConfiguration<Attachment>
{
    public void Configure(EntityTypeBuilder<Attachment> builder)
    {
        builder.ToTable("attachments", t => t.HasCheckConstraint("ck_attachments_size", "size_bytes > 0 AND size_bytes <= 10485760"));
        builder.HasKey(a => a.Id);
        builder.ConfigureAuditable();

        builder.HasOne<Organization>().WithMany().HasForeignKey(a => a.OrganizationId).OnDelete(DeleteBehavior.Restrict);

        // owner_id đa hình ⇒ không có FK; tồn tại + cùng tổ chức được kiểm khi upload (FS-BR-04).
        builder.Property(a => a.OwnerType).HasConversion<string>().HasMaxLength(24);
        builder.Property(a => a.Category).HasConversion<string>().HasMaxLength(24);
        builder.Property(a => a.StorageProvider).HasConversion<string>().HasMaxLength(16);
        builder.Property(a => a.OriginalFileName).HasMaxLength(255).IsRequired();
        builder.Property(a => a.ContentType).HasMaxLength(100).IsRequired();
        builder.Property(a => a.StorageKey).HasMaxLength(300).IsRequired();
        builder.Property(a => a.Sha256).HasMaxLength(64).IsFixedLength().IsRequired();
        builder.Property(a => a.Description).HasMaxLength(300);

        builder.HasIndex(a => a.StorageKey).IsUnique().HasDatabaseName("ux_attachments_storage_key");
        builder.HasIndex(a => new { a.OrganizationId, a.OwnerType, a.OwnerId }).HasFilter("deleted_at IS NULL").HasDatabaseName("ix_attachments_owner");
        builder.HasIndex(a => a.DeletedAt).HasFilter("deleted_at IS NOT NULL AND purged_at IS NULL").HasDatabaseName("ix_attachments_to_purge");
    }
}

internal sealed class OrganizationStorageUsageConfiguration : IEntityTypeConfiguration<OrganizationStorageUsage>
{
    public void Configure(EntityTypeBuilder<OrganizationStorageUsage> builder)
    {
        builder.ToTable("organization_storage_usage", t => t.HasCheckConstraint("ck_organization_storage_usage_non_negative", "used_bytes >= 0"));
        builder.HasKey(u => u.OrganizationId);
        builder.HasOne<Organization>().WithMany().HasForeignKey(u => u.OrganizationId).OnDelete(DeleteBehavior.Restrict);
    }
}
