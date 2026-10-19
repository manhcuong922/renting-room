using renting_room.Domain.Common;

namespace renting_room.Domain.Renters;

public static class RenterConsentErrors
{
    public static readonly Error NotFound = Error.NotFound("CONSENT_NOT_FOUND", "Không tìm thấy ghi nhận đồng ý.");
    public static readonly Error AlreadyWithdrawn = Error.Conflict("CONSENT_ALREADY_WITHDRAWN", "Ghi nhận đồng ý này đã được rút lại.");
    public static readonly Error InvalidWithdrawDate = Error.Validation("INVALID_WITHDRAW_DATE", "Ngày rút lại không được trước ngày đồng ý hoặc ở tương lai.");
}
