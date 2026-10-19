using renting_room.Domain.Properties;

namespace renting_room.Application.Rooms;

/// <summary>Lấy / cất aggregate phòng. Không chứa luật nghiệp vụ — luật nằm trong <see cref="Room"/>.</summary>
public interface IRoomRepository
{
    Task<Room?> GetAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Khóa hàng phòng tới hết transaction rồi nạp. Phải gọi trong transaction.</summary>
    Task<Room?> GetForUpdateAsync(Guid id, CancellationToken cancellationToken);

    Task<IReadOnlyList<Room>> ListActiveByPropertyAsync(Guid propertyId, CancellationToken cancellationToken);

    Task<bool> IsCodeTakenAsync(Guid propertyId, string code, Guid? excludeId, CancellationToken cancellationToken);

    /// <summary>Các mã trong <paramref name="codes"/> đã có phòng trong khu.</summary>
    Task<IReadOnlyList<string>> GetExistingCodesAsync(Guid propertyId, IReadOnlyCollection<string> codes, CancellationToken cancellationToken);

    void Add(Room room);

    void AddRange(IEnumerable<Room> rooms);
}
