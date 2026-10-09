using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using renting_room.Domain.Identity;
using renting_room.Domain.Renters;

namespace renting_room.Infrastructure.Persistence.Configurations;

internal sealed class RenterConfiguration : IEntityTypeConfiguration<Renter>
{
    public void Configure(EntityTypeBuilder<Renter> builder)
    {
        // RT-BR-01: có loại giấy tờ thì có số (và ngược lại); bắt buộc theo tuổi kiểm ở Application.
        builder.ToTable("renters", t => t.HasCheckConstraint("ck_renters_id_document", "(id_type IS NULL) = (id_number_hash IS NULL)"));
        builder.HasKey(r => r.Id);
        builder.HasAlternateKey(r => new { r.OrganizationId, r.Id }).HasName("ak_renters_organization_id_id");
        builder.ConfigureAuditable();

        builder.HasOne<Organization>().WithMany().HasForeignKey(r => r.OrganizationId).OnDelete(DeleteBehavior.Restrict);

        builder.Property(r => r.FullName).HasMaxLength(200).IsRequired();
        builder.Property(r => r.FullNameSearch).HasMaxLength(200).IsRequired();
        builder.Property(r => r.Gender).HasConversion<string>().HasMaxLength(8);
        builder.Property(r => r.Phone).HasMaxLength(16);
        builder.Property(r => r.Email).HasMaxLength(254);
        builder.Property(r => r.Nationality).HasMaxLength(2).IsFixedLength();
        builder.Property(r => r.IdType).HasConversion<string>().HasMaxLength(16);
        builder.Property(r => r.IdNumberHash).HasMaxLength(64).IsFixedLength();
        builder.Property(r => r.IdNumberLast4).HasMaxLength(4);
        builder.Property(r => r.IdIssuePlace).HasMaxLength(200);
        builder.Property(r => r.PermanentAddress).HasMaxLength(500);
        builder.Property(r => r.Occupation).HasMaxLength(200);
        builder.Property(r => r.Workplace).HasMaxLength(200);
        builder.Property(r => r.EmergencyContactName).HasMaxLength(200);
        builder.Property(r => r.EmergencyContactPhone).HasMaxLength(20);
        builder.Property(r => r.Note).HasMaxLength(2000);

        // RT-BR-02: hash đã gồm loại giấy tờ + tổ chức ⇒ unique trong tổ chức, chặn tạo trùng khi gửi song song.
        builder.HasIndex(r => new { r.OrganizationId, r.IdNumberHash }).IsUnique().HasDatabaseName(DbConstraints.RenterIdNumberUnique);
        builder.HasIndex(r => new { r.OrganizationId, r.Phone }).HasDatabaseName("ix_renters_phone");
    }
}
