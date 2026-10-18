using renting_room.Domain.Common;
using renting_room.Domain.Identity;

namespace renting_room.Application.Renters;

// Cài đặt lưu giữ của tổ chức
/// <param name="RetentionMonths">Giữ dữ liệu cá nhân bao nhiêu tháng sau lần cuối gắn với HĐ (36–120).</param>
/// <param name="AutoAnonymize">Tắt ⇒ không tự ẩn danh; chủ trọ tự chịu trách nhiệm lưu giữ.</param>
public sealed record DataRetentionDto(int RetentionMonths, bool AutoAnonymize, IReadOnlyList<Warning> Warnings);

internal static class DataRetentionMapping
{
    public static DataRetentionDto ToDto(Organization organization) => new(
        organization.PersonalDataRetentionMonths,
        organization.AutoAnonymizeEnabled,
        organization.AutoAnonymizeEnabled
            ? []
            : [new Warning("AUTO_ANONYMIZE_DISABLED",
                "Đã tắt ẩn danh tự động — dữ liệu cá nhân người thuê cũ được giữ vô thời hạn, chủ trọ tự chịu trách nhiệm lưu giữ theo luật bảo vệ dữ liệu cá nhân.")]);
}
