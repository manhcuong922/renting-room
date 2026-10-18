using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using renting_room.Domain.Identity;

namespace renting_room.Infrastructure.Persistence.Configurations;

/// <summary>M01 §11: nhật ký đăng nhập — không FK tới users (giữ dòng kể cả khi tên đăng nhập không tồn tại).</summary>
internal sealed class LoginEventConfiguration : IEntityTypeConfiguration<LoginEvent>
{
    public void Configure(EntityTypeBuilder<LoginEvent> builder)
    {
        builder.ToTable("login_events");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.UsernameHash).HasMaxLength(64).IsRequired();
        builder.Property(e => e.IpAddress).HasMaxLength(45);
        builder.Property(e => e.Reason).HasConversion<string>().HasMaxLength(32);
        builder.HasIndex(e => new { e.OrganizationId, e.OccurredAt }).HasDatabaseName("ix_login_events_organization");
        builder.HasIndex(e => e.OccurredAt).HasDatabaseName("ix_login_events_occurred_at");
    }
}
