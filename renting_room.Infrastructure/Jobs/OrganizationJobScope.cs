using renting_room.Application.Common.Interfaces;
using renting_room.Domain.Identity;

namespace renting_room.Infrastructure.Jobs;

/// <summary>
/// Job nền không có request ⇒ không có người dùng ⇒ global filter tổ chức trả rỗng và SaveChanges chặn ghi (C-01, fail-closed).
/// Job đặt <see cref="RunAs"/> NGAY sau khi tạo scope, TRƯỚC khi resolve bất cứ thứ gì dùng <see cref="ICurrentUser"/> (DbContext, handler):
/// mọi thao tác trong scope chạy dưới danh nghĩa "hệ thống" của đúng một tổ chức — vẫn bị lọc và kiểm tổ chức như request thường.
/// </summary>
public sealed class CurrentUserOverride
{
    public ICurrentUser? User { get; private set; }

    public void RunAs(ICurrentUser user)
    {
        if (User is not null)
            throw new InvalidOperationException("The current user of this scope is already overridden.");
        User = user;
    }
}

/// <summary>Hệ thống thao tác trên dữ liệu của một tổ chức: không có user (audit ghi <c>user_id</c> rỗng = hệ thống), không có IP.</summary>
public sealed record OrganizationSystemUser(Guid Organization) : ICurrentUser
{
    public bool IsAuthenticated => true;
    public Guid? UserId => null;
    public Guid? OrganizationId => Organization;
    public UserRole? Role => null;
    public string? IpAddress => null;
}
