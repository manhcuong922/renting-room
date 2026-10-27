namespace renting_room.Domain.Billing;

public enum InvoiceLineType
{
    Rent,
    Metered,
    Service,
    Surcharge,
    ManualDiscount,

    Refund,

    /// <summary>Kết toán (BL-BR-41): nợ của một hóa đơn đã chốt trước đó chuyển vào hóa đơn trả phòng — không phải phần thu (doanh thu).</summary>
    PreviousDebt,

    /// <summary>Kết toán (BL-BR-41): trừ tiền cọc đang giữ trên hóa đơn trả phòng (số âm) — không phải phần thu.</summary>
    Deposit
}
