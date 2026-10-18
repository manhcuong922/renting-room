using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using renting_room.Domain.Identity;

namespace renting_room.Infrastructure.Persistence.Configurations;

internal sealed class RefreshTokenConfiguration : IEntityTypeConfiguration<RefreshToken>
{
    public void Configure(EntityTypeBuilder<RefreshToken> builder)
    {
        builder.ToTable("refresh_tokens");
        builder.HasKey(t => t.Id);

        builder.Property(t => t.TokenHash).HasMaxLength(64).IsFixedLength().IsRequired();
        builder.Property(t => t.CreatedIp).HasMaxLength(45);
        builder.Property(t => t.RevokedReason).HasConversion<string>().HasMaxLength(32);

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(t => t.UserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(t => t.TokenHash).IsUnique().HasDatabaseName("ux_refresh_tokens_token_hash");
        builder.HasIndex(t => t.FamilyId).HasDatabaseName("ix_refresh_tokens_family_id");
        builder.HasIndex(t => t.UserId)
            .HasFilter("revoked_at IS NULL")
            .HasDatabaseName("ix_refresh_tokens_user_id_active");
    }
}
