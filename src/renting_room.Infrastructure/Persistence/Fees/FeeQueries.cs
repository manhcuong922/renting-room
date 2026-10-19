using Microsoft.EntityFrameworkCore;
using renting_room.Application.Fees;
using renting_room.Domain.Contracts;
using renting_room.Domain.Fees;

namespace renting_room.Infrastructure.Persistence.Queries;

internal sealed class FeeQueries(AppDbContext db) : IFeeQueries
{
    public async Task<FeeTypeDto?> GetAsync(Guid id, DateOnly today, CancellationToken cancellationToken) =>
        await db.FeeTypes.AsNoTracking().Include(f => f.Prices).FirstOrDefaultAsync(f => f.Id == id, cancellationToken) is { } fee
            ? FeeTypeDto.From(fee, today)
            : null;

    public async Task<IReadOnlyList<FeeTypeDto>> ListAsync(Guid propertyId, bool includeArchived, DateOnly today, CancellationToken cancellationToken)
    {
        var fees = await db.FeeTypes.AsNoTracking().Include(f => f.Prices)
            .Where(f => f.PropertyId == propertyId && (includeArchived || f.ArchivedAt == null))
            .OrderBy(f => f.SortOrder).ThenBy(f => f.Name)
            .ToListAsync(cancellationToken);
        return fees.Select(f => FeeTypeDto.From(f, today)).ToList();
    }

    public Task<bool> IsUsedByOpenContractsAsync(Guid feeTypeId, DateOnly today, CancellationToken cancellationToken) =>
        db.Contracts
            .Where(c => c.Status == ContractStatus.Draft || c.Status == ContractStatus.Active || c.Status == ContractStatus.Liquidating)
            .SelectMany(c => c.Fees)
            .AnyAsync(f => f.FeeTypeId == feeTypeId && (f.EffectiveTo == null || f.EffectiveTo >= today), cancellationToken);

    public Task<FeeType?> FindTypeAsync(Guid id, CancellationToken cancellationToken) =>
        db.FeeTypes.AsNoTracking().FirstOrDefaultAsync(f => f.Id == id, cancellationToken);

    public Task<FeeType?> GetWithPricesAsync(Guid id, CancellationToken cancellationToken) =>
        db.FeeTypes.AsNoTracking().Include(f => f.Prices).FirstOrDefaultAsync(f => f.Id == id, cancellationToken);

    public async Task<IReadOnlyDictionary<Guid, FeeType>> GetTypesAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken) =>
        await db.FeeTypes.AsNoTracking().Where(f => ids.Contains(f.Id)).ToDictionaryAsync(f => f.Id, cancellationToken);

    public async Task<IReadOnlyDictionary<Guid, FeeType>> GetTypesWithPricesAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken) =>
        await db.FeeTypes.AsNoTracking().Include(f => f.Prices).Where(f => ids.Contains(f.Id)).ToDictionaryAsync(f => f.Id, cancellationToken);

    public async Task<IReadOnlyList<FeeType>> ListAutoAttachAsync(Guid propertyId, CancellationToken cancellationToken) =>
        await db.FeeTypes.AsNoTracking()
            .Where(f => f.PropertyId == propertyId && f.AutoAttach && f.Group != FeeGroup.Metered && f.ArchivedAt == null)
            .OrderBy(f => f.SortOrder).ToListAsync(cancellationToken);

    public Task<Dictionary<string, Guid>> GetSystemFeeIdsAsync(Guid propertyId, CancellationToken cancellationToken) =>
        db.FeeTypes.Where(f => f.PropertyId == propertyId && f.SystemCode != null && f.ArchivedAt == null)
            .ToDictionaryAsync(f => f.SystemCode!, f => f.Id, cancellationToken);

    public Task<Guid?> FindElectricityIdAsync(Guid propertyId, CancellationToken cancellationToken) =>
        db.FeeTypes.Where(f => f.PropertyId == propertyId && f.SystemCode == FeeSystemCodes.Electricity && f.ArchivedAt == null)
            .Select(f => (Guid?)f.Id).FirstOrDefaultAsync(cancellationToken);

    public async Task<IReadOnlyList<FeeName>> ListNamesAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken) =>
        await db.FeeTypes.Where(f => ids.Contains(f.Id)).OrderBy(f => f.SortOrder).Select(f => new FeeName(f.Id, f.Name)).ToListAsync(cancellationToken);

    public async Task<IReadOnlyCollection<FeeType>> ListParkingFeesAsync(Guid propertyId, CancellationToken cancellationToken) =>
        await db.FeeTypes.AsNoTracking().Where(f => f.PropertyId == propertyId && f.VehicleType != null).ToListAsync(cancellationToken);

    public Task<bool> HasActiveMetersAsync(Guid feeTypeId, CancellationToken cancellationToken) =>
        db.Meters.AnyAsync(m => m.FeeTypeId == feeTypeId && m.RemovedDate == null, cancellationToken);
}
