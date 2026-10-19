using renting_room.Domain.Contracts;

namespace renting_room.Application.Contracts;

public interface IContractTemplateQueries
{
    /// <summary>Chỉ đọc.</summary>
    Task<ContractTemplate?> GetAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Theo loại rồi tên.</summary>
    Task<IReadOnlyList<ContractTemplate>> ListAsync(ContractType? type, bool includeArchived, CancellationToken cancellationToken);
}
