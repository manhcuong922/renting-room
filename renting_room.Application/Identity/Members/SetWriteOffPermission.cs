using FluentValidation;
using Mediator;
using Microsoft.EntityFrameworkCore;
using renting_room.Application.Common.Interfaces;
using renting_room.Domain.Common;

namespace renting_room.Application.Identity.Members;

// Sửa / khóa / mở / cấp lại mật khẩu / gỡ (chỉ chủ trọ, chỉ phó quản lý)
/// <summary>PM-BR-16: chủ trọ bật / tắt quyền bỏ nợ cho từng phó quản lý — audit tự ghi (Updated User, <c>canWriteOff</c>).</summary>
public sealed record SetWriteOffPermissionCommand(Guid Id, bool Allowed) : IRequest<Result<MemberDto>>;

public sealed class SetWriteOffPermissionHandler(IAppDbContext db, ICurrentUser currentUser)
    : IRequestHandler<SetWriteOffPermissionCommand, Result<MemberDto>>
{
    public async ValueTask<Result<MemberDto>> Handle(SetWriteOffPermissionCommand request, CancellationToken cancellationToken)
    {
        var manager = await ManagerLookup.FindAsync(db, currentUser, request.Id, cancellationToken);
        if (manager.IsFailure)
            return manager.Error!;

        var changed = manager.Value!.SetWriteOffPermission(request.Allowed);
        if (changed.IsFailure)
            return changed.Error!;

        await db.SaveChangesAsync(cancellationToken);
        return await db.Users.AsNoTracking().Where(u => u.Id == manager.Value.Id).ToDto().FirstAsync(cancellationToken);
    }
}
