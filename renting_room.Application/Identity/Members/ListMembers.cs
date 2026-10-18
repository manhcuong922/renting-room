using FluentValidation;
using Mediator;
using Microsoft.EntityFrameworkCore;
using renting_room.Application.Common.Interfaces;
using renting_room.Domain.Identity;

namespace renting_room.Application.Identity.Members;

// Queries (chủ trọ + phó quản lý)
public sealed record ListMembersQuery(bool IncludeRemoved = false) : IRequest<IReadOnlyList<MemberDto>>;

public sealed class ListMembersHandler(IAppDbContext db, ICurrentUser currentUser)
    : IRequestHandler<ListMembersQuery, IReadOnlyList<MemberDto>>
{
    public async ValueTask<IReadOnlyList<MemberDto>> Handle(ListMembersQuery request, CancellationToken cancellationToken)
    {
        var organizationId = currentUser.OrganizationId;
        return await db.Users.AsNoTracking()
            .Where(u => u.OrganizationId == organizationId && (request.IncludeRemoved || u.Status != UserStatus.Removed))
            .OrderBy(u => u.Role).ThenBy(u => u.FullName)
            .ToDto()
            .ToListAsync(cancellationToken);
    }
}
