using renting_room.Domain.Contracts;

namespace renting_room.Application.Contracts;

public interface IContractTemplateQueries
{
    Task<ContractTemplate?> GetAsync(Guid id, CancellationToken cancellationToken);

    Task<IReadOnlyList<ContractTemplate>> ListAsync(ContractType? type, bool includeArchived, CancellationToken cancellationToken);
}
