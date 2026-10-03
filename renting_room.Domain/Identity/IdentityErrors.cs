using renting_room.Domain.Common;

namespace renting_room.Domain.Identity;

public static class IdentityErrors
{
    public static readonly Error InvalidCredentials =
        Error.Unauthorized("INVALID_CREDENTIALS",
            "Tên đăng nhập hoặc mật khẩu không đúng. Nhập sai 5 lần liên tiếp, tài khoản sẽ bị tạm khóa 15 phút.");

    public static readonly Error AccountLocked =
        Error.Locked("ACCOUNT_LOCKED", "Tài khoản đang bị khóa. Vui lòng thử lại sau hoặc liên hệ quản trị viên.");

    public static readonly Error OrganizationSuspended =
        Error.Forbidden("ORGANIZATION_SUSPENDED", "Tổ chức của bạn đang bị tạm ngưng. Vui lòng liên hệ quản trị viên.");

    public static readonly Error InvalidRefreshToken =
        Error.Unauthorized("INVALID_REFRESH_TOKEN", "Phiên đăng nhập không hợp lệ hoặc đã hết hạn. Vui lòng đăng nhập lại.");

    public static readonly Error InvalidCurrentPassword =
        Error.BusinessRule("INVALID_CURRENT_PASSWORD", "Mật khẩu hiện tại không đúng.");

    public static readonly Error PasswordContainsUsername =
        Error.Validation("WEAK_PASSWORD", "Mật khẩu không được chứa tên đăng nhập.");

    public static readonly Error PasswordReused =
        Error.BusinessRule("PASSWORD_REUSED", "Mật khẩu mới phải khác mật khẩu hiện tại.");

    public static readonly Error UserNotFound =
        Error.NotFound("USER_NOT_FOUND", "Không tìm thấy người dùng.");

    public static readonly Error OrganizationNotFound =
        Error.NotFound("ORGANIZATION_NOT_FOUND", "Không tìm thấy tổ chức.");

    public static readonly Error OrganizationCodeTaken =
        Error.Conflict("ORG_CODE_TAKEN", "Mã tổ chức đã tồn tại.");

    public static readonly Error PhoneTaken =
        Error.Conflict("PHONE_TAKEN", "Số điện thoại đã được sử dụng.");

    public static readonly Error EmailTaken =
        Error.Conflict("EMAIL_TAKEN", "Email đã được sử dụng.");

    public static readonly Error OrganizationAlreadySuspended =
        Error.Conflict("ORG_ALREADY_SUSPENDED", "Tổ chức đã ở trạng thái tạm ngưng.");

    public static readonly Error OrganizationNotSuspended =
        Error.Conflict("ORG_NOT_SUSPENDED", "Tổ chức không ở trạng thái tạm ngưng.");

    public static readonly Error TemporaryPasswordExpired =
        Error.Unauthorized("TEMPORARY_PASSWORD_EXPIRED",
            "Mật khẩu tạm đã hết hạn. Vui lòng liên hệ người cấp tài khoản để được cấp lại.");

    public static readonly Error MemberNotFound =
        Error.NotFound("MEMBER_NOT_FOUND", "Không tìm thấy thành viên trong tổ chức.");

    public static readonly Error ManagerLimitReached =
        Error.BusinessRule("MANAGER_LIMIT_REACHED", "Tổ chức đã đạt số lượng phó quản lý tối đa.");

    public static readonly Error CannotModifyOwner =
        Error.BusinessRule("CANNOT_MODIFY_OWNER", "Không thể thực hiện thao tác này với tài khoản chủ trọ.");

    public static readonly Error CannotLockOwner =
        Error.BusinessRule("CANNOT_LOCK_OWNER", "Không khóa tài khoản chủ trọ — hãy tạm ngưng tổ chức.");

    public static readonly Error UserRemoved =
        Error.Conflict("USER_REMOVED", "Tài khoản đã bị gỡ khỏi tổ chức.");

    public static readonly Error UserAlreadyLocked =
        Error.Conflict("USER_ALREADY_LOCKED", "Tài khoản đang bị khóa.");

    public static readonly Error UserNotLocked =
        Error.Conflict("USER_NOT_LOCKED", "Tài khoản không ở trạng thái khóa.");
}
