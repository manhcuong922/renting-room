namespace renting_room.Domain.Contracts;

public enum DepositStatus
{
    Holding,

    Refunded,

    Transferred,

    /// <summary>Không hoàn cọc (mất cọc theo điều khoản) — PM-BR-33, lý do bắt buộc.</summary>
    Forfeited,

    /// <summary>Đã trừ vào hóa đơn trả phòng khi chốt (M07 BL-BR-41) — hủy hóa đơn trả phòng ⇒ về đang giữ.</summary>
    Deducted
}
