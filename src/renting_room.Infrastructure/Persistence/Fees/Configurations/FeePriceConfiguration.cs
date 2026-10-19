using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using renting_room.Domain.Fees;

namespace renting_room.Infrastructure.Persistence.Configurations;

internal sealed class FeePriceConfiguration : IEntityTypeConfiguration<FeePrice>
{
    private static readonly JsonSerializerOptions TierJson = new(JsonSerializerDefaults.Web);

    public void Configure(EntityTypeBuilder<FeePrice> builder)
    {
        builder.ToTable("fee_prices", t => t.HasCheckConstraint("ck_fee_prices_unit_price", "unit_price >= 0"));
        builder.HasKey(p => p.Id);
        builder.ConfigureAuditable();

        builder.Property(p => p.UnitPrice).HasColumnType("numeric(18,2)");
        builder.Property(p => p.Note).HasMaxLength(300);
        builder.Ignore(p => p.IsTiered);
        builder.Ignore(p => p.HighestPrice);
        builder.Property(p => p.Tiers)
            .HasColumnType("jsonb")
            .HasConversion(
                v => v == null ? null : JsonSerializer.Serialize(v, TierJson),
                v => v == null ? null : JsonSerializer.Deserialize<List<PriceTier>>(v, TierJson),
                new ValueComparer<List<PriceTier>?>(
                    (a, b) => (a == null && b == null) || (a != null && b != null && a.SequenceEqual(b)),
                    v => v == null ? 0 : v.Aggregate(0, (hash, t) => HashCode.Combine(hash, t)),
                    v => v == null ? null : v.ToList()));

        builder.HasIndex(p => new { p.FeeTypeId, p.EffectiveFrom }).IsUnique().HasDatabaseName(DbConstraints.FeePriceDateUnique);
    }
}
