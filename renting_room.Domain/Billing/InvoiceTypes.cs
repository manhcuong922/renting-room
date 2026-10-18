namespace renting_room.Domain.Billing;

public enum InvoiceType
{
    Regular,
    Final
}

public enum InvoiceStatus
{
    Draft,
    Finalized,
    Void
}

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

/// <summary>Vấn đề của phiếu nháp: <c>Error</c> chặn chốt (thiếu chỉ số, thiếu giá…), <c>Warning</c> chỉ nhắc.</summary>
public sealed record InvoiceIssue(string Code, string Severity, string Message, Guid? Ref = null)
{
    public const string Error = "Error";
    public const string Warning = "Warning";
}
