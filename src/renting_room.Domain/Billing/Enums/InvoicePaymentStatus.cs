namespace renting_room.Domain.Billing;

/// <summary>Trạng thái thu tiền (dẫn xuất, chỉ phiếu đã chốt).</summary>
public enum InvoicePaymentStatus
{
    Unpaid,
    PartiallyPaid,
    Paid,
    Overdue,

    /// <summary>Đã đóng bằng "bỏ nợ" (PM-BR-16) — không tính doanh thu.</summary>
    WrittenOff,

    /// <summary>Tổng âm (hoàn trả lớn hơn phần thu) — chủ trọ phải trả lại người thuê, chưa xác nhận (BL-BR-27).</summary>
    RefundPending,

    /// <summary>Tổng âm, chủ trọ đã xác nhận trả lại người thuê.</summary>
    Refunded
}
