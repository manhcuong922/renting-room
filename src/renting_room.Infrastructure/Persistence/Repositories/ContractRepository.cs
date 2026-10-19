using Microsoft.EntityFrameworkCore;
using renting_room.Application.Contracts;
using renting_room.Domain.Contracts;

namespace renting_room.Infrastructure.Persistence.Repositories;

internal sealed class ContractRepository(AppDbContext db) : IContractRepository
{
    public Task<Contract?> GetForUpdateAsync(Guid id, CancellationToken cancellationToken) =>
        db.Contracts
            .Include(c => c.RentTerms)
            .Include(c => c.Occupants)
            .Include(c => c.Assets)
            .Include(c => c.Vehicles)
            .Include(c => c.Fees)
            .Include(c => c.RoomMoves)
            .Include(c => c.RepresentativeHistory)
            .AsSplitQuery()
            .FirstOrDefaultAsync(c => c.Id == id, cancellationToken);

    public Task<Contract?> FindForUpdateAsync(Guid id, CancellationToken cancellationToken) =>
        db.Contracts.FirstOrDefaultAsync(c => c.Id == id, cancellationToken);

    public void Add(Contract contract) => db.Contracts.Add(contract);

    public async Task<IReadOnlyList<Contract>> ListWithVehiclesByRenterAsync(Guid renterId, CancellationToken cancellationToken) =>
        await db.Contracts.Include(c => c.Vehicles)
            .Where(c => c.RepresentativeRenterId == renterId || c.Vehicles.Any(v => v.RenterId == renterId)).ToListAsync(cancellationToken);
}

internal sealed class ContractTemplateRepository(AppDbContext db) : IContractTemplateRepository
{
    public Task<ContractTemplate?> GetForUpdateAsync(Guid id, CancellationToken cancellationToken) =>
        db.ContractTemplates.FirstOrDefaultAsync(t => t.Id == id, cancellationToken);

    public Task<bool> IsNameTakenAsync(string name, Guid? excludeId, CancellationToken cancellationToken) =>
        excludeId is { } id
            ? db.ContractTemplates.AnyAsync(t => t.Name == name && t.Id != id, cancellationToken)
            : db.ContractTemplates.AnyAsync(t => t.Name == name, cancellationToken);

    public void Add(ContractTemplate template) => db.ContractTemplates.Add(template);
}

internal sealed class ContractTemplateQueries(AppDbContext db) : IContractTemplateQueries
{
    public Task<ContractTemplate?> GetAsync(Guid id, CancellationToken cancellationToken) =>
        db.ContractTemplates.AsNoTracking().FirstOrDefaultAsync(t => t.Id == id, cancellationToken);

    public async Task<IReadOnlyList<ContractTemplate>> ListAsync(ContractType? type, bool includeArchived, CancellationToken cancellationToken)
    {
        var query = db.ContractTemplates.AsNoTracking();
        if (type is { } t)
            query = query.Where(x => x.ContractType == t);
        if (!includeArchived)
            query = query.Where(x => x.ArchivedAt == null);
        return await query.OrderBy(x => x.ContractType).ThenBy(x => x.Name).ToListAsync(cancellationToken);
    }
}
