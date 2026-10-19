namespace renting_room.Domain.Properties;

/// <summary>Trạng thái hiển thị — DẪN XUẤT từ hợp đồng, không lưu (PR-BR-02).</summary>
public enum RoomDisplayStatus
{
    Vacant,
    Reserved,
    Occupied,
    Maintenance,
    Archived
}
