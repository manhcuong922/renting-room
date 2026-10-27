namespace renting_room.Domain.Contracts;

public enum ContractFlag
{
    RepresentativeMovedOut,

    NoOccupantLeft,

    ExpiredAwaitingDecision,

    MissingSignedDocument,

    /// <summary>Đã qua ngày dự kiến trả phòng mà chưa thanh lý — kỳ cuối chưa tính theo ngày ở (M05 CT-UC-07).</summary>
    MoveOutOverdue
}
