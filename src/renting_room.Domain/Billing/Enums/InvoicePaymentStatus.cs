namespace renting_room.Domain.Billing;

public enum InvoicePaymentStatus
{
    Unpaid,
    PartiallyPaid,
    Paid,
    Overdue,

    WrittenOff,

    RefundPending,

    Refunded,

    /// <summary>Chủ trọ quyết định không hoàn (vd người thuê bỏ đi) — không có chi tiền, có lý do ở ghi chú hoàn.</summary>
    RefundWaived,

    /// <summary>Nợ đã chuyển vào hóa đơn trả phòng (BL-BR-41), hóa đơn trả phòng chưa xong — "Chờ thu ở hóa đơn trả phòng"; không còn nợ ở hóa đơn này.</summary>
    CarriedOver,

    /// <summary>Nợ đã chuyển, hóa đơn trả phòng đã xong (thu đủ / đã hoàn / không hoàn) — "Hoàn thành (qua hóa đơn trả phòng)".</summary>
    CarriedSettled,

    /// <summary>Nợ đã chuyển, hóa đơn trả phòng kết thúc bằng bỏ nợ — "Đã bỏ nợ (qua hóa đơn trả phòng)".</summary>
    CarriedWrittenOff
}
