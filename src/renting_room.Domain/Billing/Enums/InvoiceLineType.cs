namespace renting_room.Domain.Billing;

/// <summary>Nhóm dòng trên phiếu (M07): Tiền phòng · Điện nước · Dịch vụ · Phụ thu · Giảm trừ · Hoàn trả.</summary>
public enum InvoiceLineType
{
    Rent,
    Metered,
    Service,
    Surcharge,
    ManualDiscount,

    /// <summary>Hoàn trả cho người thuê (BL-BR-27) — nhập tay như phụ thu, lưu số âm, được làm tổng phiếu âm.</summary>
    Refund
}
