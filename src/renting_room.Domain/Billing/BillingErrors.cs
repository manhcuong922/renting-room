using renting_room.Domain.Common;

namespace renting_room.Domain.Billing;

public static class BillingErrors
{
    public static readonly Error NotFound = Error.NotFound("INVOICE_NOT_FOUND", "Không tìm thấy phiếu tiền phòng.");
    public static readonly Error LineNotFound = Error.NotFound("INVOICE_LINE_NOT_FOUND", "Không tìm thấy dòng phiếu.");
    public static readonly Error NotDraft = Error.BusinessRule("INVOICE_NOT_DRAFT", "Phiếu đã chốt / đã hủy — không sửa được.");
    public static readonly Error NotFinalized = Error.BusinessRule("INVOICE_NOT_FINALIZED", "Phiếu chưa chốt.");
    public static readonly Error NoteRequired = Error.Validation("NOTE_REQUIRED", "Nhập lý do / ghi chú.");
    public static readonly Error RefundSourceRequired = Error.Validation("REFUND_SOURCE_REQUIRED",
        "Hoàn trả chỉ dùng khi phiếu đã chốt bị tính sai mà người thuê đã trả — chọn phiếu đó.");
    public static readonly Error RefundSourceInvalid = Error.BusinessRule("REFUND_SOURCE_INVALID",
        "Phiếu nguồn phải là phiếu đã chốt của cùng hợp đồng.");

    public static Error RefundExceedsPaid(decimal available) =>
        Error.BusinessRule("REFUND_EXCEEDS_PAID", $"Chỉ hoàn được tối đa {available:N0}đ — số người thuê đã trả thật cho phiếu nguồn trừ các lần hoàn trước.")
            .WithDetail("available", available);
    public static readonly Error NegativeTotal = Error.BusinessRule("NEGATIVE_TOTAL",
        "Giảm trừ không được lớn hơn phần thu — muốn trả lại tiền cho người thuê thì dùng dòng Hoàn trả.");
    public static readonly Error NothingToRefund = Error.BusinessRule("NOTHING_TO_REFUND", "Phiếu không có khoản phải trả lại người thuê.");
    public static readonly Error RefundConfirmed = Error.BusinessRule("REFUND_CONFIRMED",
        "Phiếu đã xác nhận trả lại tiền cho người thuê — bỏ xác nhận trước.");
    public static readonly Error RefundNotConfirmed = Error.BusinessRule("REFUND_NOT_CONFIRMED", "Phiếu chưa xác nhận đã hoàn tiền.");
    public static readonly Error InvalidRefundDate = Error.Validation("INVALID_REFUND_DATE", "Ngày hoàn từ ngày lập phiếu tới hôm nay.");

    public static Error RefundPending(decimal amount) =>
        Error.BusinessRule("REFUND_PENDING", $"Còn {amount:N0}đ phải trả lại người thuê — xác nhận đã hoàn trên phiếu trước.")
            .WithDetail("refundDue", amount);
    public static readonly Error ConcurrencyConflict = Error.Conflict("CONCURRENCY_CONFLICT", "Phiếu vừa được người khác sửa — tải lại.");
    public static readonly Error SaveFailed = Error.Conflict("INVOICE_SAVE_FAILED", "Không lưu được phiếu — tải lại rồi thử lại.");
    public static readonly Error HasPayments = Error.BusinessRule("INVOICE_HAS_PAYMENTS", "Phiếu đã thu tiền — đảo phiếu thu trước khi hủy.");
    public static readonly Error NotLatest = Error.BusinessRule("NOT_LATEST_INVOICE",
        "Chỉ hủy / xóa được phiếu mới nhất của hợp đồng — hủy các phiếu kỳ sau trước.");
    public static readonly Error DraftStale = Error.Conflict("DRAFT_STALE",
        "Dữ liệu nguồn (giá, chỉ số, người ở…) đã đổi sau khi tạo nháp — tính lại rồi chốt.");
    public static readonly Error FinalExists = Error.Conflict("FINAL_INVOICE_EXISTS", "Hợp đồng đã có phiếu quyết toán — mở phiếu đó để sửa.");
    public static readonly Error FinalRequired = Error.BusinessRule("FINAL_INVOICE_REQUIRED", "Lập và chốt phiếu quyết toán trước khi hoàn tất thanh lý.");
    public static readonly Error FinalFinalized = Error.BusinessRule("FINAL_INVOICE_FINALIZED",
        "Phiếu quyết toán đã chốt — hủy phiếu quyết toán trước khi hủy thanh lý.");
    public static readonly Error DraftExists = Error.Conflict("INVOICE_DRAFT_EXISTS", "Hợp đồng còn phiếu nháp — chốt hoặc xóa trước.");
    public static readonly Error PreviousNotBilled = Error.BusinessRule("PREVIOUS_PERIOD_NOT_BILLED", "Chưa lập phiếu kỳ trước — lập lần lượt từng kỳ.");

    public static Error HasDebt(decimal outstanding) =>
        Error.BusinessRule("CONTRACT_HAS_DEBT", $"Hợp đồng còn nợ {outstanding:N0}đ — chọn \"Đã thu toàn bộ\" hoặc \"Bỏ nợ\".")
            .WithDetail("outstanding", outstanding);

    public static readonly Error InvalidBillingMonth = Error.Validation("INVALID_BILLING_MONTH", "Tháng thu dạng yyyy-MM.");

    public static Error HasIssues(IReadOnlyCollection<string> codes) =>
        Error.BusinessRule("INVOICE_HAS_ISSUES", "Phiếu còn vấn đề chưa xử lý (thiếu chỉ số / thiếu giá…).").WithDetail("issues", codes);
}
