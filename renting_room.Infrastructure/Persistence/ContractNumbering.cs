using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using renting_room.Application.Common.Interfaces;

namespace renting_room.Infrastructure.Persistence;

/// <summary>Bộ đếm số hợp đồng theo (tổ chức, năm).</summary>
public sealed class ContractNumberSequence
{
    public Guid OrganizationId { get; set; }
    public int Year { get; set; }
    public int LastValue { get; set; }
}

internal sealed class ContractNumberSequenceConfiguration : IEntityTypeConfiguration<ContractNumberSequence>
{
    public void Configure(EntityTypeBuilder<ContractNumberSequence> builder)
    {
        builder.ToTable("contract_number_sequences");
        builder.HasKey(s => new { s.OrganizationId, s.Year });
    }
}

/// <summary>
/// UPSERT … RETURNING trong 1 câu lệnh: PostgreSQL khóa hàng bộ đếm nên 2 request song song luôn nhận 2 số khác nhau.
/// Số bị "nhảy" nếu tạo hợp đồng thất bại sau khi lấy số — chấp nhận được (unique index vẫn đảm bảo không trùng).
/// </summary>
public sealed class ContractNumberGenerator(AppDbContext db) : IContractNumberGenerator
{
    public async Task<string> NextAsync(Guid organizationId, int year, CancellationToken cancellationToken)
    {
        var values = await db.Database.SqlQuery<int>($"""
            INSERT INTO contract_number_sequences (organization_id, year, last_value)
            VALUES ({organizationId}, {year}, 1)
            ON CONFLICT (organization_id, year) DO UPDATE SET last_value = contract_number_sequences.last_value + 1
            RETURNING last_value AS "Value"
            """).ToListAsync(cancellationToken);

        return $"HD{year}-{values[0]:0000}";
    }
}
