using renting_room.Domain.Common;

namespace renting_room.Domain.Fees;

public static class FeeErrors
{
    public static readonly Error NotFound = Error.NotFound("FEE_TYPE_NOT_FOUND", "Không tìm thấy khoản thu.");
    public static readonly Error NameTaken = Error.Conflict("FEE_NAME_TAKEN", "Tên khoản thu đã có trong khu.");
    public static readonly Error SystemCodeTaken = Error.Conflict("FEE_SYSTEM_CODE_TAKEN", "Khu đã có khoản thu điện / nước hệ thống đang dùng.");
    public static readonly Error Archived = Error.BusinessRule("FEE_ARCHIVED", "Khoản thu đã ngừng dùng.");
    public static readonly Error AlreadyArchived = Error.Conflict("FEE_ALREADY_ARCHIVED", "Khoản thu đã ngừng dùng.");
    public static readonly Error NotArchived = Error.Conflict("FEE_NOT_ARCHIVED", "Khoản thu đang được dùng.");
    public static readonly Error HasActiveMeters = Error.BusinessRule("FEE_HAS_ACTIVE_METERS",
        "Còn công tơ đang hoạt động cho khoản này — tháo / thay công tơ trước khi ngừng dùng.");
    public static readonly Error InUse = Error.BusinessRule("FEE_IN_USE",
        "Khoản thu đang gắn với hợp đồng còn hiệu lực — gỡ khỏi các hợp đồng trước khi ngừng dùng.");
    public static readonly Error PriceNotFound = Error.NotFound("FEE_PRICE_NOT_FOUND", "Không tìm thấy bản giá.");
    public static readonly Error PriceDateExists = Error.Conflict("FEE_PRICE_DATE_EXISTS", "Đã có bản giá hiệu lực từ ngày này.");
    public static readonly Error PriceLocked = Error.BusinessRule("FEE_PRICE_LOCKED",
        "Ngày hiệu lực thuộc kỳ đã chốt phiếu — chỉ đổi giá từ kỳ chưa chốt.");
    public static readonly Error TiersForMeteredOnly = Error.Validation("FEE_TIERS_METERED_ONLY",
        "Giá theo bậc chỉ dùng cho điện nước theo công tơ.");
    public static readonly Error MeteredFollowsRoom = Error.Validation("FEE_METERED_FOLLOWS_ROOM",
        "Điện / nước theo công tơ tính theo công tơ của phòng — không gắn vào hợp đồng.");
    public static readonly Error NotInProperty = Error.BusinessRule("FEE_NOT_IN_PROPERTY", "Khoản thu không thuộc khu của hợp đồng.");
}
