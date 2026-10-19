using Microsoft.EntityFrameworkCore;
using renting_room.Application.Fees;
using renting_room.Domain.Fees;

namespace renting_room.Infrastructure.Persistence.Repositories;

internal sealed class FeeTypeRepository(AppDbContext db) : IFeeTypeRepository
{
    public Task<FeeType?> GetAsync(Guid id, CancellationToken cancellationToken) =>
        db.FeeTypes.Include(f => f.Prices).FirstOrDefaultAsync(f => f.Id == id, cancellationToken);

    public async Task<FeeType?> GetForUpdateAsync(Guid id, CancellationToken cancellationToken)
    {
        await db.LockForUpdateAsync<FeeType>(id, cancellationToken);
        return await GetAsync(id, cancellationToken);
    }

    public async Task<IReadOnlyList<FeeType>> ListActiveByPropertyAsync(Guid propertyId, CancellationToken cancellationToken) =>
        await db.FeeTypes.Include(f => f.Prices)
            .Where(f => f.PropertyId == propertyId && f.ArchivedAt == null)
            .ToListAsync(cancellationToken);

    public Task<bool> IsNameTakenAsync(
        Guid propertyId, string normalizedName, string? systemCode, Guid? excludeId, CancellationToken cancellationToken) =>
        db.FeeTypes.AnyAsync(f => f.PropertyId == propertyId && f.ArchivedAt == null
            && (excludeId == null || f.Id != excludeId.Value)
            && (f.NameNormalized == normalizedName || (systemCode != null && f.SystemCode == systemCode)), cancellationToken);

    public void Add(FeeType fee) => db.FeeTypes.Add(fee);
}
