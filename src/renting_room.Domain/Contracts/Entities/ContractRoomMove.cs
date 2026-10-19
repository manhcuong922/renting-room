using renting_room.Domain.Common;

namespace renting_room.Domain.Contracts;

/// <summary>
/// CT-BR-14: lịch sử chuyển phòng — HĐ đã ở phòng <see cref="RoomId"/> trong [<see cref="FromDate"/>, <see cref="ToDate"/>] (ngày chuyển đi tính là
/// còn ở). Phòng hiện tại nằm trên HĐ (<c>room_id</c>, <c>room_since</c>). Bất biến sau khi ghi.
/// </summary>
public sealed class ContractRoomMove : TenantEntity
{
    private ContractRoomMove() { } // EF Core

    internal ContractRoomMove(Guid contractId, Guid roomId, DateOnly fromDate, DateOnly toDate)
    {
        if (toDate < fromDate)
            throw new ArgumentOutOfRangeException(nameof(toDate));
        Id = Guid.CreateVersion7();
        ContractId = contractId;
        RoomId = roomId;
        FromDate = fromDate;
        ToDate = toDate;
    }

    public Guid ContractId { get; private set; }
    public Guid RoomId { get; private set; }
    public DateOnly FromDate { get; private set; }
    public DateOnly ToDate { get; private set; }

    /// <summary>CT-BR-14: những gì lần chuyển đi khỏi phòng này đã đổi (giá thuê, dịch vụ, khoản chênh lệch) — để hủy chuyển phòng hoàn nguyên. JSON.</summary>
    public string? UndoSnapshot { get; private set; }

    internal bool SetUndoSnapshot(string snapshotJson)
    {
        if (UndoSnapshot == snapshotJson)
            return false;
        UndoSnapshot = snapshotJson;
        return true;
    }
}
