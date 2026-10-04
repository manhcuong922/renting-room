using Microsoft.EntityFrameworkCore;
using renting_room.Application.Common.Interfaces;
using renting_room.Domain.Contracts;

namespace renting_room.Application.Contracts;

/// <summary>
/// CT-BR-44/45: cờ của nhiều HĐ cho danh sách HĐ / danh sách phòng — nạp HĐ đang hiệu lực kèm người ở rồi dùng đúng
/// <see cref="Contract.Flags"/> của domain (một nguồn quy tắc duy nhất, không viết lại bằng SQL).
/// </summary>
internal static class ContractFlagReader
{
    public static async Task<IReadOnlyDictionary<Guid, IReadOnlyList<ContractFlag>>> LoadAsync(
        IAppDbContext db, IEnumerable<Guid> contractIds, DateOnly today, CancellationToken ct)
    {
        var ids = contractIds.Distinct().ToList();
        if (ids.Count == 0)
            return new Dictionary<Guid, IReadOnlyList<ContractFlag>>();

        var contracts = await db.Contracts.AsNoTracking().Include(c => c.Occupants)
            .Where(c => ids.Contains(c.Id) && c.Status == ContractStatus.Active)
            .ToListAsync(ct);
        return contracts.ToDictionary(c => c.Id, c => c.Flags(today));
    }
}
