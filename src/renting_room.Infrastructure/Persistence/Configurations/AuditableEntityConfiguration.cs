using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using renting_room.Domain.Common;

namespace renting_room.Infrastructure.Persistence.Configurations;

internal static class AuditableEntityConfiguration
{
    /// <summary>Cột audit + <c>Version</c> ánh xạ cột hệ thống <c>xmin</c> làm concurrency token (C-07).</summary>
    public static void ConfigureAuditable<TEntity>(this EntityTypeBuilder<TEntity> builder)
        where TEntity : AuditableEntity
    {
        builder.Property(e => e.CreatedAt).IsRequired();
        builder.Property(e => e.Version).IsRowVersion();
    }
}
