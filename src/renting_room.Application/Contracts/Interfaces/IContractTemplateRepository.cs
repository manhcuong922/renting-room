using renting_room.Domain.Contracts;

namespace renting_room.Application.Contracts;

/// <summary>Lấy / cất mẫu hợp đồng.</summary>
public interface IContractTemplateRepository
{
    Task<ContractTemplate?> GetForUpdateAsync(Guid id, CancellationToken cancellationToken);

    Task<bool> IsNameTakenAsync(string name, Guid? excludeId, CancellationToken cancellationToken);

    void Add(ContractTemplate template);
}
