using renting_room.Domain.Contracts;

namespace renting_room.Application.Contracts;

public interface IContractRepository
{
    Task<Contract?> GetForUpdateAsync(Guid id, CancellationToken cancellationToken);

    Task<Contract?> FindForUpdateAsync(Guid id, CancellationToken cancellationToken);

    Task<Contract?> FindResignDraftAsync(Guid previousContractId, CancellationToken cancellationToken);

    void Add(Contract contract);

    Task<IReadOnlyList<Contract>> ListWithVehiclesByRenterAsync(Guid renterId, CancellationToken cancellationToken);
}
