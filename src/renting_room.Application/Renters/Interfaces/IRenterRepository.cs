using renting_room.Domain.Renters;

namespace renting_room.Application.Renters;

public interface IRenterRepository
{
    Task<Renter?> GetForUpdateAsync(Guid id, CancellationToken cancellationToken);

    Task<Guid?> FindIdByIdNumberHashAsync(string hash, Guid? excludeId, CancellationToken cancellationToken);

    void Add(Renter renter);
}
