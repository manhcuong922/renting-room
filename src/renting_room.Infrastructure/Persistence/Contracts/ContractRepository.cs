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
