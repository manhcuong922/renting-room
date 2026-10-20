using renting_room.Domain.Fees;

namespace renting_room.Application.Fees;

public sealed record FeeName(Guid Id, string Name);

public interface IFeeQueries
{
    Task<FeeTypeDto?> GetAsync(Guid id, DateOnly today, CancellationToken cancellationToken);

    Task<IReadOnlyList<FeeTypeDto>> ListAsync(Guid propertyId, bool includeArchived, DateOnly today, CancellationToken cancellationToken);

    Task<bool> IsUsedByOpenContractsAsync(Guid feeTypeId, DateOnly today, CancellationToken cancellationToken);

    Task<bool> HasActiveMetersAsync(Guid feeTypeId, CancellationToken cancellationToken);

    Task<FeeType?> FindTypeAsync(Guid id, CancellationToken cancellationToken);

    Task<FeeType?> GetWithPricesAsync(Guid id, CancellationToken cancellationToken);

    Task<IReadOnlyDictionary<Guid, FeeType>> GetTypesAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken);

    Task<IReadOnlyDictionary<Guid, FeeType>> GetTypesWithPricesAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken);

    Task<IReadOnlyList<FeeType>> ListAutoAttachAsync(Guid propertyId, CancellationToken cancellationToken);

    Task<Dictionary<string, Guid>> GetSystemFeeIdsAsync(Guid propertyId, CancellationToken cancellationToken);

    Task<Guid?> FindElectricityIdAsync(Guid propertyId, CancellationToken cancellationToken);

    /// <summary>Tên các khoản thu, theo thứ tự hiển thị.</summary>
    Task<IReadOnlyList<FeeName>> ListNamesAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken);

    Task<IReadOnlyCollection<FeeType>> ListParkingFeesAsync(Guid propertyId, CancellationToken cancellationToken);
}
