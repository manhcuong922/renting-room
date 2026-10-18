using FluentValidation;
using Mediator;
using Microsoft.EntityFrameworkCore;
using renting_room.Application.Common.Interfaces;
using renting_room.Domain.Common;

namespace renting_room.Application.Identity.Members;

// Sửa / khóa / mở / cấp lại mật khẩu / gỡ (chỉ chủ trọ, chỉ phó quản lý)
/// <summary>
/// ID-BR-22: chủ trọ cấp / thu hồi quyền xem dữ liệu nhạy cảm cho phó quản lý — audit tự ghi khi lưu
/// (Updated User, <c>canViewSensitiveData</c> cũ → mới).
/// </summary>
public sealed record SetSensitiveDataAccessCommand(Guid Id, bool Allowed) : IRequest<Result<MemberDto>>;

public sealed class SetSensitiveDataAccessHandler(IAppDbContext db, ICurrentUser currentUser)
    : IRequestHandler<SetSensitiveDataAccessCommand, Result<MemberDto>>
{
    public async ValueTask<Result<MemberDto>> Handle(SetSensitiveDataAccessCommand request, CancellationToken cancellationToken)
    {
        var manager = await ManagerLookup.FindAsync(db, currentUser, request.Id, cancellationToken);
        if (manager.IsFailure)
            return manager.Error!;

        var user = manager.Value!;
        var changed = user.SetSensitiveDataAccess(request.Allowed);
        if (changed.IsFailure)
            return changed.Error!;

        await db.SaveChangesAsync(cancellationToken);
        return await db.Users.AsNoTracking().Where(u => u.Id == user.Id).ToDto().FirstAsync(cancellationToken);
    }
}
