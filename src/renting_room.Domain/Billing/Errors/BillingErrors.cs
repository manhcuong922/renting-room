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
    public static readonly Error GapNotSupportedPrepaid = Error.BusinessRule("GAP_NOT_SUPPORTED_PREPAID",
        "Bỏ qua các kỳ chưa lập chỉ dùng được cho khu thu sau.");
    public static readonly Error GapPreviousNotFinalized = Error.BusinessRule("GAP_PREVIOUS_NOT_FINALIZED",
        "Phiếu liền trước các kỳ bỏ qua còn là nháp — chốt hoặc xóa nháp trước.");
    public static readonly Error GapTooLong = Error.BusinessRule("GAP_TOO_LONG",
        $"Chỉ bỏ qua được tối đa {ContractBillingGap.MaxPeriods} kỳ một lần — kiểm tra lại tháng cần lập.");
    public static readonly Error GapSpansScheduleChange = Error.BusinessRule("GAP_SPANS_SCHEDULE_CHANGE",
        "Các kỳ bỏ qua có đổi ngày chốt của khu — không bỏ qua được.");
    public static readonly Error ExternalSettlementNotAllowed = Error.BusinessRule("EXTERNAL_SETTLEMENT_NOT_ALLOWED",
        "Kỳ cuối đã có phiếu — lập hóa đơn trả phòng thay vì \"đã xử lý ngoài hệ thống\".");
    public static readonly Error ExternalNoteRequired = Error.BusinessRule("EXTERNAL_NOTE_REQUIRED",
        "Nhập ghi chú đã xử lý ngoài hệ thống (thu bằng cách nào, đến ngày nào).");
    public static readonly Error NotLatest = Error.BusinessRule("NOT_LATEST_INVOICE",
        "Chỉ hủy / xóa được phiếu mới nhất của hợp đồng — hủy các phiếu kỳ sau trước.");
    public static readonly Error DraftStale = Error.Conflict("DRAFT_STALE",
        "Dữ liệu nguồn (giá, chỉ số, người ở…) đã đổi sau khi tạo nháp — tính lại rồi chốt.");
    public static readonly Error FinalExists = Error.Conflict("FINAL_INVOICE_EXISTS", "Hợp đồng đã có hóa đơn trả phòng — mở hóa đơn đó để sửa.");
    public static readonly Error FinalRequired = Error.BusinessRule("FINAL_INVOICE_REQUIRED", "Lập và chốt hóa đơn trả phòng trước khi hoàn tất thanh lý.");
    public static readonly Error FinalNotRequired = Error.BusinessRule("FINAL_INVOICE_NOT_REQUIRED",
        "Người thuê trả phòng trước ngày tính tiền — không có kỳ nào cần lập hóa đơn trả phòng, hoàn tất thanh lý luôn.");
    public static readonly Error FinalFinalized = Error.BusinessRule("FINAL_INVOICE_FINALIZED",
        "Hóa đơn trả phòng đã chốt — hủy hóa đơn trả phòng trước khi hủy thanh lý.");
    public static readonly Error PriceMissing = Error.BusinessRule("FEE_PRICE_MISSING", "Khoản thu chưa có giá cho kỳ của hóa đơn.");
    public static readonly Error DraftExists = Error.Conflict("INVOICE_DRAFT_EXISTS", "Hợp đồng còn phiếu nháp — chốt hoặc xóa trước.");
    public static readonly Error PreviousNotBilled = Error.BusinessRule("PREVIOUS_PERIOD_NOT_BILLED", "Chưa lập phiếu kỳ trước — lập lần lượt từng kỳ.");

    public static Error PreviousNotBilledPeriods(object unbilledPeriods) => PreviousNotBilled.WithDetail("unbilledPeriods", unbilledPeriods);
    public static readonly Error SettlementLineLocked = Error.BusinessRule("SETTLEMENT_LINE_LOCKED",
        "Dòng kết toán (nợ chuyển / trừ cọc) do hệ thống tính — không sửa / xóa tay. Mất cọc ⇒ bấm \"Không trả cọc\"; giữ lại một phần cọc ⇒ thêm phụ thu.");
    public static readonly Error InvoiceCarriedOver = Error.BusinessRule("INVOICE_CARRIED_OVER",
        "Nợ của hóa đơn này đã chuyển vào hóa đơn trả phòng — hủy hóa đơn trả phòng trước.");
    public static readonly Error RefundWaiveReasonRequired = Error.Validation("REFUND_WAIVE_REASON_REQUIRED", "Nhập lý do không hoàn tiền cho người thuê.");
    public static readonly Error RefundWaiveNotAllowed = Error.Forbidden("REFUND_WAIVE_NOT_ALLOWED", "Chỉ chủ trọ được quyết định không hoàn tiền cho người thuê.");

    public static Error PendingCharges(int count, decimal surcharge, decimal credit) =>
        Error.BusinessRule("PENDING_CHARGES",
                $"Còn {count} khoản chưa xử lý (phụ thu {surcharge:N0}đ, bù {credit:N0}đ) — xác nhận coi như đã thu / đã trả tay khi hoàn tất.")
            .WithDetail("count", count).WithDetail("surcharge", surcharge).WithDetail("credit", credit);

    public static Error HasDebt(decimal outstanding) =>
        Error.BusinessRule("CONTRACT_HAS_DEBT", $"Hợp đồng còn nợ {outstanding:N0}đ — chọn \"Đã thu toàn bộ\" hoặc \"Bỏ nợ\".")
            .WithDetail("outstanding", outstanding);

    public static readonly Error InvalidBillingMonth = Error.Validation("INVALID_BILLING_MONTH", "Tháng thu dạng yyyy-MM.");

    public static Error HasIssues(IReadOnlyCollection<string> codes) =>
        Error.BusinessRule("INVOICE_HAS_ISSUES", "Phiếu còn vấn đề chưa xử lý (thiếu chỉ số / thiếu giá…).").WithDetail("issues", codes);
}
