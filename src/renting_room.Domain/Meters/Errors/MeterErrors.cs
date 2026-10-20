using renting_room.Domain.Common;

namespace renting_room.Domain.Meters;

public static class MeterErrors
{
    public static readonly Error NotFound = Error.NotFound("METER_NOT_FOUND", "Không tìm thấy công tơ.");
    public static readonly Error ReadingNotFound = Error.NotFound("READING_NOT_FOUND", "Không tìm thấy chỉ số.");
    public static readonly Error AlreadyActive = Error.Conflict("METER_ALREADY_ACTIVE",
        "Phòng đã có công tơ đang hoạt động cho khoản này — thay công tơ thay vì lắp thêm.");
    public static readonly Error FeeNotMetered = Error.BusinessRule("FEE_NOT_METERED", "Chỉ khoản điện nước theo công tơ mới lắp công tơ.");
    public static readonly Error Removed = Error.BusinessRule("METER_REMOVED", "Công tơ đã tháo.");
    public static readonly Error ContractReadingExists = Error.Conflict("CONTRACT_READING_EXISTS",
        "Hợp đồng đã có chỉ số nhận phòng / chỉ số cuối trên công tơ này trong ngày — tải lại.");
    public static readonly Error InvalidReadingDate = Error.BusinessRule("INVALID_READING_DATE",
        "Ngày ghi chỉ số phải trong thời gian công tơ hoạt động và không trước chỉ số đã ghi.");

    public static Error NotMonotonic(decimal? previousValue, decimal? nextValue) =>
        Error.BusinessRule("READING_NOT_MONOTONIC",
                $"Chỉ số phải ≥ chỉ số trước{(previousValue is { } p ? $" ({p:0.##})" : "")}" +
                $"{(nextValue is { } n ? $" và ≤ chỉ số sau ({n:0.##})" : "")}.")
            .WithDetail("previousValue", previousValue)
            .WithDetail("nextValue", nextValue);

    public static Error HandoverReadingRequired(IReadOnlyCollection<Guid> meterIds) =>
        Error.BusinessRule("HANDOVER_READING_REQUIRED",
                "Nhập chỉ số nhận phòng cho từng công tơ của phòng (hoặc chọn \"Dùng số mới nhất\").")
            .WithDetail("meterIds", meterIds);

    public static Error FinalReadingRequired(IReadOnlyCollection<Guid> meterIds) =>
        Error.BusinessRule("FINAL_READING_REQUIRED", "Nhập chỉ số cuối cho từng công tơ của phòng tại ngày trả phòng.")
            .WithDetail("meterIds", meterIds);

    public static readonly Error ReadingNotVoidable = Error.BusinessRule("READING_NOT_VOIDABLE",
        "Chỉ hủy được chỉ số cuối kỳ / kiểm tra. Chỉ số lắp, tháo, nhận phòng, cuối hợp đồng: sửa giá trị hoặc hủy thao tác gốc.");

    public static readonly Error ReadingLocked = Error.BusinessRule("READING_LOCKED",
        "Chỉ số đã dùng cho phiếu đã chốt — dùng \"Điều chỉnh chỉ số đã chốt\" (tạo khoản bù / phụ thu vào kỳ sau).");

    public static readonly Error ReadingNotLocked = Error.BusinessRule("READING_NOT_LOCKED",
        "Chỉ số chưa dùng cho phiếu đã chốt — sửa trực tiếp, không cần điều chỉnh.");
    public static readonly Error ReadingNotAdjustable = Error.BusinessRule("READING_NOT_ADJUSTABLE",
        "Chỉ điều chỉnh được chỉ số cuối của đoạn đo trên phiếu đã chốt.");
    public static readonly Error ReadingNextPeriodFinalized = Error.BusinessRule("READING_NEXT_PERIOD_FINALIZED",
        "Kỳ sau đã chốt phiếu bắt đầu từ chỉ số này — tổng 2 kỳ đã tự bù nhau, không cần điều chỉnh.");
    public static readonly Error AdjustmentHandlingRequired = Error.Validation("ADJUSTMENT_HANDLING_REQUIRED",
        "Hóa đơn đã thu tiền — chọn \"Xử lý ngay\" (tính lại trên hóa đơn) hoặc \"Đẩy sang hóa đơn sau\".");
    public static readonly Error AdjustmentAlreadyBilled = Error.BusinessRule("ADJUSTMENT_ALREADY_BILLED",
        "Khoản điều chỉnh lần trước đã thanh toán (đã trả / thu ngay, hoặc đã nằm trên hóa đơn đã chốt) — không điều chỉnh tiếp chỉ số này được; " +
        "phần chênh còn lại xử lý bằng khoản phụ thu / bù nhập tay.");
    public static readonly Error AdjustmentNoNextInvoice = Error.BusinessRule("ADJUSTMENT_NO_NEXT_INVOICE",
        "Hóa đơn trả phòng đã chốt — không còn hóa đơn sau, chọn \"Xử lý ngay\".");
    public static readonly Error AdjustmentContractEnded = Error.BusinessRule("CONTRACT_ENDED",
        "Hợp đồng đã kết thúc — chưa hỗ trợ điều chỉnh chỉ số sau khi thanh lý.");
    public static readonly Error ReadingUnchanged = Error.Validation("READING_UNCHANGED", "Chỉ số mới trùng chỉ số hiện tại.");
    public static readonly Error AdjustmentTooLarge = Error.BusinessRule("READING_ADJUSTMENT_TOO_LARGE",
        "Tiền chênh quá lớn — kiểm tra lại chỉ số.");

    public static Error PeriodLocked(DateOnly lockedUntil) =>
        Error.BusinessRule("METER_PERIOD_LOCKED",
                $"Kỳ sử dụng tới {lockedUntil:dd/MM/yyyy} đã chốt phiếu — chọn ngày thay / tháo từ {lockedUntil.AddDays(1):dd/MM/yyyy}.")
            .WithDetail("lockedUntil", lockedUntil);

    public static readonly Error UnknownMeter = Error.BusinessRule("METER_NOT_IN_ROOM", "Công tơ không thuộc phòng của hợp đồng hoặc không hoạt động tại ngày này.");
}
