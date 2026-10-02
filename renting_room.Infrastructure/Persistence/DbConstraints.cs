using renting_room.Domain.Common;
using renting_room.Domain.Identity;

namespace renting_room.Infrastructure.Persistence;

/// <summary>
/// Tên constraint/index trong DB và lỗi nghiệp vụ tương ứng — để khi unique index chặn ghi trùng lúc chạy song song,
/// API trả đúng mã lỗi (VD PHONE_TAKEN) thay vì lỗi chung.
/// </summary>
public static class DbConstraints
{
    public const string OrganizationCodeUnique = "ux_organizations_code";
    public const string UserPhoneUnique = "ux_users_phone";
    public const string UserEmailUnique = "ux_users_email";
    public const string OrganizationOwnerUnique = "ux_users_organization_owner";

    private static readonly Dictionary<string, Error> UniqueViolationErrors = new(StringComparer.Ordinal)
    {
        [OrganizationCodeUnique] = IdentityErrors.OrganizationCodeTaken,
        [UserPhoneUnique] = IdentityErrors.PhoneTaken,
        [UserEmailUnique] = IdentityErrors.EmailTaken
    };

    public static Error? FromUniqueViolation(string? constraintName) =>
        constraintName is not null && UniqueViolationErrors.TryGetValue(constraintName, out var error) ? error : null;
}
