using renting_room.Domain.Common;

namespace renting_room.Domain.Renters;

public static class RenterErrors
{
    public static readonly Error RenterNotFound = Error.NotFound("RENTER_NOT_FOUND", "Không tìm thấy người thuê.");
    public static readonly Error Anonymized = Error.BusinessRule("RENTER_ANONYMIZED",
        "Hồ sơ đã ẩn danh — không còn dữ liệu cá nhân; người này thuê lại thì tạo hồ sơ mới.");
    public static readonly Error HasOpenContract = Error.BusinessRule("RENTER_HAS_ACTIVE_CONTRACT",
        "Người này còn hợp đồng nháp / đang hiệu lực / đang thanh lý — kết thúc hoặc hủy trước khi ẩn danh.");
    public static readonly Error HasUnsettledInvoices = Error.BusinessRule("RENTER_HAS_UNSETTLED_INVOICES",
        "Hợp đồng người này đứng tên còn phiếu chưa thu đủ hoặc chờ hoàn tiền — xử lý xong trước khi ẩn danh.");
    public static readonly Error IdNumberExists = Error.Conflict("RENTER_ID_NUMBER_EXISTS",
        "Số giấy tờ đã có trong hệ thống — hãy dùng lại hồ sơ cũ (một người được đứng tên nhiều phòng).");

    /// <summary>RT-BR-02: kèm <c>existingRenterId</c> để UI mở / chọn hồ sơ cũ.</summary>
    public static readonly Error IdNumberRequired = Error.Validation("ID_NUMBER_REQUIRED", "Từ 14 tuổi phải có số giấy tờ.");

    public static Error IdNumberExistsFor(Guid existingRenterId) => IdNumberExists.WithDetail("existingRenterId", existingRenterId);
}
