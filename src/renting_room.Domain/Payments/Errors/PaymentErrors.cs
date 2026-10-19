using renting_room.Domain.Common;

namespace renting_room.Domain.Payments;

public static class PaymentErrors
{
    public static readonly Error NotFound = Error.NotFound("PAYMENT_NOT_FOUND", "Không tìm thấy phiếu thu.");
    public static readonly Error AlreadyReversed = Error.Conflict("PAYMENT_ALREADY_REVERSED", "Phiếu thu đã đảo.");
    public static readonly Error ExceedsDebt = Error.BusinessRule("PAYMENT_EXCEEDS_DEBT",
        "Số tiền lớn hơn số còn nợ — nhập đúng số tiền còn nợ (chưa hỗ trợ trả thừa).");
    public static readonly Error ContractNotBillable = Error.BusinessRule("CONTRACT_NOT_BILLABLE",
        "Hợp đồng đã kết thúc — không ghi / đảo phiếu thu (hoàn tất thanh lý đã xử lý hết nợ).");
    public static readonly Error PaidBeforeContract = Error.Validation("INVALID_PAID_AT", "Ngày thu không được trước ngày bắt đầu hợp đồng quá 60 ngày.");
    public static readonly Error NoDebt = Error.BusinessRule("NO_OUTSTANDING_INVOICE", "Hợp đồng không còn phiếu nào chưa thu đủ.");
}
