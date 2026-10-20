using renting_room.Domain.Common;

namespace renting_room.Domain.Contracts;

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

    public string? UndoSnapshot { get; private set; }

    internal bool SetUndoSnapshot(string snapshotJson)
    {
        if (UndoSnapshot == snapshotJson)
            return false;
        UndoSnapshot = snapshotJson;
        return true;
    }
}
