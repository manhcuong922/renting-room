using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using renting_room.Application.Common.Interfaces;

namespace renting_room.Infrastructure.Persistence;

/// <summary>Bộ đếm số chứng từ theo (tổ chức, loại, năm): <c>PB</c> phiếu báo tiền phòng, <c>PT</c> phiếu thu.</summary>
public sealed class DocumentNumberSequence
{
    public Guid OrganizationId { get; set; }
    public string Prefix { get; set; } = null!;
    public int Year { get; set; }
    public int LastValue { get; set; }
}

internal sealed class DocumentNumberSequenceConfiguration : IEntityTypeConfiguration<DocumentNumberSequence>
{
    public void Configure(EntityTypeBuilder<DocumentNumberSequence> builder)
    {
        builder.ToTable("document_number_sequences");
        builder.HasKey(s => new { s.OrganizationId, s.Prefix, s.Year });
        builder.Property(s => s.Prefix).HasMaxLength(4);
    }
}

/// <summary>UPSERT … RETURNING: 2 request song song luôn nhận 2 số khác nhau (như số hợp đồng). Định dạng <c>PB2026-000001</c>.</summary>
public sealed class DocumentNumberGenerator(AppDbContext db) : IDocumentNumberGenerator
{
    public async Task<string> NextAsync(Guid organizationId, string prefix, int year, CancellationToken cancellationToken)
    {
        var values = await db.Database.SqlQuery<int>($"""
            INSERT INTO document_number_sequences (organization_id, prefix, year, last_value)
            VALUES ({organizationId}, {prefix}, {year}, 1)
            ON CONFLICT (organization_id, prefix, year) DO UPDATE SET last_value = document_number_sequences.last_value + 1
            RETURNING last_value AS "Value"
            """).ToListAsync(cancellationToken);
        return $"{prefix}{year}-{values[0]:000000}";
    }
}
