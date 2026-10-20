using renting_room.Domain.Common;

namespace renting_room.Domain.Billing;

public static class RoomChargeErrors
{
    public static readonly Error NotFound = Error.NotFound("ROOM_CHARGE_NOT_FOUND", "Không tìm thấy khoản phát sinh.");
    public static readonly Error NoTenant = Error.BusinessRule("ROOM_CHARGE_NO_TENANT",
        "Phòng không có người thuê tại ngày phát sinh — khoản phụ thu / bù chỉ ghi cho phòng đang có người thuê.");
    public static readonly Error NoOpenInvoice = Error.BusinessRule("ROOM_CHARGE_NO_OPEN_INVOICE",
        "Hợp đồng đã chốt hóa đơn trả phòng / đã kết thúc — không còn hóa đơn để đưa khoản này vào.");
    public static readonly Error Locked = Error.BusinessRule("ROOM_CHARGE_LOCKED",
        "Khoản đã nằm trên phiếu đã chốt — không sửa / hủy được (hủy phiếu trước nếu cần).");
    public static readonly Error Cancelled = Error.BusinessRule("ROOM_CHARGE_CANCELLED", "Khoản phát sinh đã hủy.");

    public static Error CreditTooHigh(decimal limit) => Error.BusinessRule("ROOM_CHARGE_CREDIT_TOO_HIGH",
            $"Các khoản bù chưa trả của phòng không vượt một tháng tiền phòng ({limit:N0}đ) — bù nhiều hơn thì trả trực tiếp rồi đánh dấu đã trả.")
        .WithDetail("limit", limit);
    public static readonly Error System = Error.BusinessRule("ROOM_CHARGE_SYSTEM",
        "Khoản do hệ thống tạo (chốt điện nước khi chuyển phòng / điều chỉnh chỉ số) — không sửa / hủy tay; chỉ đánh dấu đã trả / đã thu ngay. Khoản điều chỉnh chỉ số đổi bằng cách điều chỉnh lại chỉ số.");
    public static readonly Error NotSettled = Error.BusinessRule("ROOM_CHARGE_NOT_SETTLED", "Khoản chưa đánh dấu đã thanh toán.");
    public static readonly Error InvalidSettledDate = Error.Validation("INVALID_SETTLED_DATE", "Ngày thanh toán không được ở tương lai.");
    public static readonly Error InvalidIncurredDate = Error.Validation("INVALID_INCURRED_DATE", "Ngày phát sinh không được ở tương lai.");
}
