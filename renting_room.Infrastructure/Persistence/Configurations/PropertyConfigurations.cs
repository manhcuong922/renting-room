using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using renting_room.Domain.Identity;
using renting_room.Domain.Properties;

namespace renting_room.Infrastructure.Persistence.Configurations;

internal sealed class PropertyConfiguration : IEntityTypeConfiguration<Property>
{
    public void Configure(EntityTypeBuilder<Property> builder)
    {
        builder.ToTable("properties", t =>
        {
            t.HasCheckConstraint("ck_properties_anchor_day", "default_billing_anchor_day BETWEEN 1 AND 31");
            t.HasCheckConstraint("ck_properties_rent_cycle", "default_rent_cycle_months IN (1, 2, 3, 6, 12)");
            t.HasCheckConstraint("ck_properties_lessor_organization",
                "lessor_type IS DISTINCT FROM 'Organization' OR (lessor_tax_code IS NOT NULL AND lessor_representative_name IS NOT NULL)");
        });

        builder.HasKey(p => p.Id);
        // Đích của FK composite (C-01): bảng con trỏ tới (organization_id, id) ⇒ DB chặn liên kết chéo tổ chức.
        builder.HasAlternateKey(p => new { p.OrganizationId, p.Id }).HasName("ak_properties_organization_id_id");
        builder.ConfigureAuditable();

        builder.HasOne<Organization>().WithMany().HasForeignKey(p => p.OrganizationId).OnDelete(DeleteBehavior.Restrict);

        builder.Property(p => p.Code).HasMaxLength(32).IsRequired();
        builder.Property(p => p.Name).HasMaxLength(200).IsRequired();
        builder.Property(p => p.StreetAddress).HasMaxLength(300).IsRequired();
        builder.Property(p => p.CommuneName).HasMaxLength(100).IsRequired();
        builder.Property(p => p.ProvinceName).HasMaxLength(100).IsRequired();
        builder.Property(p => p.CommuneCode).HasMaxLength(10);
        builder.Property(p => p.ProvinceCode).HasMaxLength(10);
        builder.Property(p => p.Description).HasMaxLength(2000);
        builder.Property(p => p.EvnCustomerCode).HasMaxLength(20);
        builder.Property(p => p.DefaultChargeMode).HasConversion<string>().HasMaxLength(16);
        builder.Property(p => p.DefaultProrationMode).HasConversion<string>().HasMaxLength(16);

        builder.Property(p => p.LessorType).HasConversion<string>().HasMaxLength(16);
        builder.Property(p => p.LessorName).HasMaxLength(200);
        builder.Property(p => p.LessorAddress).HasMaxLength(500);
        builder.Property(p => p.LessorPhone).HasMaxLength(15);
        builder.Property(p => p.LessorEmail).HasMaxLength(254);
        builder.Property(p => p.LessorIdType).HasConversion<string>().HasMaxLength(16);
        builder.Property(p => p.LessorIdNumberLast4).HasMaxLength(4);
        builder.Property(p => p.LessorIdIssuePlace).HasMaxLength(200);
        builder.Property(p => p.LessorTaxCode).HasMaxLength(14);
        builder.Property(p => p.LessorRepresentativeName).HasMaxLength(200);
        builder.Property(p => p.LessorRepresentativeTitle).HasMaxLength(100);
        builder.Property(p => p.AuthorizationDocNo).HasMaxLength(50);
        builder.Property(p => p.LandParcelNo).HasMaxLength(20);
        builder.Property(p => p.LandMapSheetNo).HasMaxLength(20);
        builder.Property(p => p.OwnershipCertificateNo).HasMaxLength(50);
        builder.Property(p => p.BankName).HasMaxLength(100);
        builder.Property(p => p.BankAccountNo).HasMaxLength(20);
        builder.Property(p => p.BankAccountName).HasMaxLength(200);
        builder.Property(p => p.HouseRulesText).HasMaxLength(20_000);

        builder.Ignore(p => p.BillingDefaults);
        builder.Ignore(p => p.Address);
        builder.Ignore(p => p.Lessor);
        builder.Ignore(p => p.BankAccount);

        builder.HasIndex(p => new { p.OrganizationId, p.Code }).IsUnique().HasDatabaseName(DbConstraints.PropertyCodeUnique);
    }
}

internal sealed class RoomConfiguration : IEntityTypeConfiguration<Room>
{
    public void Configure(EntityTypeBuilder<Room> builder)
    {
        builder.ToTable("rooms", t =>
        {
            t.HasCheckConstraint("ck_rooms_max_occupants", $"max_occupants BETWEEN 1 AND {Room.MaxOccupantsLimit}");
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

internal sealed class RoomGroupMemberConfiguration : IEntityTypeConfiguration<RoomGroupMember>
{
    public void Configure(EntityTypeBuilder<RoomGroupMember> builder)
    {
        builder.ToTable("room_group_members");
        builder.HasKey(m => m.Id);
        builder.ConfigureAuditable();

        // PR-BR-07: phòng phải cùng khu với nhóm — đảm bảo bằng FK composite có property_id.
        builder.HasOne<Room>()
            .WithMany()
            .HasForeignKey(m => new { m.OrganizationId, m.PropertyId, m.RoomId })
            .HasPrincipalKey(r => new { r.OrganizationId, r.PropertyId, r.Id })
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(m => new { m.RoomGroupId, m.RoomId }).IsUnique().HasDatabaseName("ux_room_group_members_group_room");
    }
}
