using renting_room.Application.Common.Models;
using renting_room.Application.Identity;
using renting_room.Application.Identity.Me;
using renting_room.Application.Identity.Members;
using renting_room.Domain.Identity;

namespace renting_room.Infrastructure.Persistence.Queries;

internal static class MemberDtoProjection
{
    public static IQueryable<MemberDto> ToMemberDto(this IQueryable<User> users) =>
        users.Select(u => new MemberDto(
            u.Id,
            u.FullName,
            u.PhoneNormalized,
            u.Status == UserStatus.Removed ? null : u.EmailNormalized,
            u.Role,
            u.Status,
            u.MustChangePassword,
            u.TempPasswordExpiresAt,
            u.LastLoginAt,
            u.CreatedAt,
            u.Role == UserRole.OrgOwner || u.CanViewSensitiveData,
            u.Version.ToString(),
            u.Role == UserRole.OrgOwner || u.CanWriteOff));
}
