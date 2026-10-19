using FluentValidation;
using Mediator;
using renting_room.Application.Common.Interfaces;
using renting_room.Domain.Common;

namespace renting_room.Application.Identity.Members;

/// <summary>
/// ID-BR-22: chủ trọ cấp / thu hồi quyền xem dữ liệu nhạy cảm cho phó quản lý — audit tự ghi khi lưu
/// (Updated User, <c>canViewSensitiveData</c> cũ → mới).
/// </summary>
public sealed record SetSensitiveDataAccessCommand(Guid Id, bool Allowed) : IRequest<Result<MemberDto>>;

public sealed class SetSensitiveDataAccessHandler(IUserRepository users, IUserQueries userQueries, IUnitOfWork unitOfWork, ICurrentUser currentUser)
    : IRequestHandler<SetSensitiveDataAccessCommand, Result<MemberDto>>
{
    public async ValueTask<Result<MemberDto>> Handle(SetSensitiveDataAccessCommand request, CancellationToken cancellationToken)
    {
        var manager = await ManagerLookup.FindAsync(users, currentUser, request.Id, cancellationToken);
        if (manager.IsFailure)
            return manager.Error!;

        var user = manager.Value!;
        var changed = user.SetSensitiveDataAccess(request.Allowed);
        if (changed.IsFailure)
            return changed.Error!;

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return (await userQueries.GetMemberAsync(user.Id, user.OrganizationId, cancellationToken))!;
    }
}
