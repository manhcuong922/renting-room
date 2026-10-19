using renting_room.Domain.Renters;

namespace renting_room.Application.Renters;

public interface IRenterRepository
{
    Task<Renter?> GetForUpdateAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>RT-BR-02: hồ sơ khác đã dùng số giấy tờ này (so theo hash).</summary>
    Task<Guid?> FindIdByIdNumberHashAsync(string hash, Guid? excludeId, CancellationToken cancellationToken);

    void Add(Renter renter);
}
