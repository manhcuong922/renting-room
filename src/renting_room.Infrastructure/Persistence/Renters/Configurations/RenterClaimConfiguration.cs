using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using renting_room.Domain.Identity;
using renting_room.Domain.Renters;

namespace renting_room.Infrastructure.Persistence.Configurations;

/// <summary>M01 ID-BR-26 (08/11/2026): quản lý khu đã nhập đúng số giấy tờ của hồ sơ ngoài phạm vi ⇒ thấy hồ sơ đó.</summary>
internal sealed class RenterClaimConfiguration : IEntityTypeConfiguration<RenterClaim>
{
    public void Configure(EntityTypeBuilder<RenterClaim> builder)
    {
        builder.ToTable("renter_claims");
        builder.HasKey(c => c.Id);
        builder.ConfigureAuditable();

        builder.HasOne<Renter>()
            .WithMany()
            .HasForeignKey(c => new { c.OrganizationId, c.RenterId })
            .HasPrincipalKey(r => new { r.OrganizationId, r.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany().HasForeignKey(c => c.UserId).OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(c => new { c.RenterId, c.UserId }).IsUnique().HasDatabaseName("ux_renter_claims_renter_user");
    }
}
