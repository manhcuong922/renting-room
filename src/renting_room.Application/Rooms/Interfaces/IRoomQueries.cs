using renting_room.Application.Common.Models;
using renting_room.Domain.Properties;

namespace renting_room.Application.Rooms;

public sealed record RoomCodeRow(Guid Id, string Code);

public sealed record RoomCodeState(Guid Id, string Code, DateTimeOffset? ArchivedAt);

public sealed record RoomPropertyCode(string RoomCode, string PropertyCode);

public interface IRoomQueries
{
    Task<RoomDto?> GetAsync(Guid id, DateOnly today, CancellationToken cancellationToken);

    Task<PagedResult<RoomDto>> ListAsync(ListRoomsQuery request, DateOnly today, CancellationToken cancellationToken);

    Task<bool> ExistsAsync(Guid id, CancellationToken cancellationToken);

    Task<bool> HasOpenContractAsync(Guid roomId, bool includeDraft, DateOnly today, CancellationToken cancellationToken);

    /// <summary>Số phòng trong <paramref name="roomIds"/> thuộc khu <paramref name="propertyId"/>.</summary>
    Task<int> CountInPropertyAsync(IReadOnlyCollection<Guid> roomIds, Guid propertyId, CancellationToken cancellationToken);

    Task<IReadOnlyDictionary<Guid, string>> GetCodesAsync(IReadOnlyCollection<Guid> roomIds, CancellationToken cancellationToken);

    Task<string?> GetCodeAsync(Guid roomId, CancellationToken cancellationToken);

    /// <summary>Số phòng trong <paramref name="ids"/> có trong tổ chức.</summary>
    Task<int> CountAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken);

    /// <summary>Số nhóm phòng trong <paramref name="ids"/> có trong tổ chức.</summary>
    Task<int> CountGroupsAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken);

    Task<HashSet<string>> GetCodeSetAsync(Guid propertyId, CancellationToken cancellationToken);

    Task<IReadOnlyDictionary<string, RoomCodeState>> ListByCodesAsync(
        Guid propertyId, IReadOnlyCollection<string> codes, CancellationToken cancellationToken);

    Task<Room?> FindAsync(Guid roomId, CancellationToken cancellationToken);

    Task<Guid?> GetPropertyIdAsync(Guid roomId, CancellationToken cancellationToken);

    Task<IReadOnlyDictionary<Guid, string>> GetCodesInPropertyAsync(
        Guid propertyId, IReadOnlyCollection<Guid>? roomIds, string? floor, CancellationToken cancellationToken);

    /// <summary>Phòng trong <paramref name="roomIds"/> trừ <paramref name="excludeIds"/>.</summary>
    Task<IReadOnlyList<RoomCodeRow>> ListCodesAsync(
        IReadOnlyCollection<Guid> roomIds, IReadOnlyCollection<Guid> excludeIds, CancellationToken cancellationToken);

    Task<IReadOnlyList<Room>> ListByPropertyAsync(Guid propertyId, string? floor, CancellationToken cancellationToken);

    /// <summary>Phòng (chỉ đọc) trong <paramref name="roomIds"/> thuộc khu.</summary>
    Task<IReadOnlyDictionary<Guid, Room>> ListInPropertyAsync(Guid propertyId, IReadOnlyCollection<Guid> roomIds, CancellationToken cancellationToken);

    Task<IReadOnlyDictionary<Guid, Room>> ListAsync(IReadOnlyCollection<Guid> roomIds, CancellationToken cancellationToken);

    Task<IReadOnlyDictionary<Guid, RoomPropertyCode>> GetCodesWithPropertyAsync(IReadOnlyCollection<Guid> roomIds, CancellationToken cancellationToken);

    Task<IReadOnlyList<RoomGroupDto>> ListGroupsAsync(Guid propertyId, CancellationToken cancellationToken);
}
