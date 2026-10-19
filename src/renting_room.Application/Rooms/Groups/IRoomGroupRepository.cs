using renting_room.Domain.Properties;

namespace renting_room.Application.Rooms;

/// <summary>Lấy / cất aggregate nhóm phòng (gồm danh sách phòng thành viên).</summary>
public interface IRoomGroupRepository
{
    Task<RoomGroup?> GetAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Tên đã có ở nhóm khác của khu (không phân biệt hoa thường).</summary>
    Task<bool> IsNameTakenAsync(Guid propertyId, string name, Guid? excludeId, CancellationToken cancellationToken);

    void Add(RoomGroup group);

    void Remove(RoomGroup group);
}
