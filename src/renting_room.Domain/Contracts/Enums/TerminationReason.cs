namespace renting_room.Domain.Contracts;

/// <summary>CT-BR-21: lý do kết thúc hợp đồng.</summary>
public enum TerminationReason
{
    /// <summary>Hết hạn — chỉ cho HĐ có thời hạn, ngày trả phòng từ ngày hết hạn trở đi (CT-BR-21).</summary>
    Expired,
    MutualAgreement,
    LesseeUnilateral,
    LessorUnilateral,
    RoomTransfer,

    /// <summary>Bên thuê tự ý bỏ đi không báo (trả phòng bất chợt) — ngày trả phòng = ngày phát hiện / ngày cuối còn ở (CT-BR-41).</summary>
    Abandoned
}
