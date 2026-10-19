namespace renting_room.Domain.Contracts;

/// <summary>Căn cứ bên cho thuê chấm dứt — Luật Nhà ở 2023 Điều 172 khoản 2; HĐ không thời hạn: báo trước 90 ngày (CT-BR-42).</summary>
public enum TerminationGround
{
    RentArrears3Months,
    WrongPurpose,
    UnauthorizedRenovation,
    Other,

    /// <summary>HĐ không thời hạn: bên cho thuê thông báo chấm dứt, HĐ chấm dứt sau 90 ngày (CT-BR-42).</summary>
    IndefiniteTermNotice
}
