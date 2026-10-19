namespace renting_room.Domain.Contracts;

/// <summary>Cờ dẫn xuất theo ngày của HĐ đang hiệu lực — cần chủ trọ xử lý (CT-BR-44, CT-BR-45).</summary>
public enum ContractFlag
{
    /// <summary>Người đứng tên đã chuyển đi, vẫn còn người ở — cần ký lại.</summary>
    RepresentativeMovedOut,

    /// <summary>Không còn ai ở mà HĐ vẫn hiệu lực — thanh lý?</summary>
    NoOccupantLeft,

    /// <summary>Đã quá ngày hết hạn, chủ trọ chưa quyết định (gia hạn / ở tiếp / thu lại phòng).</summary>
    ExpiredAwaitingDecision,

    /// <summary>Chưa có bản hợp đồng đã ký (giấy / ảnh / PDF) — chỉ để nhắc, không ảnh hưởng thu tiền.</summary>
    MissingSignedDocument
}
