using Microsoft.EntityFrameworkCore;
using renting_room.Application.Common.Interfaces;
using renting_room.Domain.Common;
using renting_room.Domain.Identity;

namespace renting_room.Application.Identity.Members;

internal static class ManagerLookup
{
    /// <summary>Phó quản lý cùng tổ chức với người gọi. Khác tổ chức ⇒ 404 (không lộ tồn tại); là chủ trọ ⇒ 422.</summary>
    public static async Task<Result<User>> FindAsync(IAppDbContext db, ICurrentUser currentUser, Guid id, CancellationToken ct)
    {
        var organizationId = currentUser.OrganizationId;
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == id && u.OrganizationId == organizationId, ct);
        if (user is null)
            return IdentityErrors.MemberNotFound;
        if (user.Role != UserRole.OrgManager)
            return IdentityErrors.CannotModifyOwner;

        return user;
    }
}
