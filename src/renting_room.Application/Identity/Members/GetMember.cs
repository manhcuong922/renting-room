using FluentValidation;
using Mediator;
using Microsoft.EntityFrameworkCore;
using renting_room.Application.Common.Interfaces;
using renting_room.Domain.Common;
using renting_room.Domain.Identity;

namespace renting_room.Application.Identity.Members;

public sealed record GetMemberQuery(Guid Id) : IRequest<Result<MemberDto>>;

public sealed class GetMemberHandler(IAppDbContext db, ICurrentUser currentUser) : IRequestHandler<GetMemberQuery, Result<MemberDto>>
{
    public async ValueTask<Result<MemberDto>> Handle(GetMemberQuery request, CancellationToken cancellationToken)
    {
        var organizationId = currentUser.OrganizationId;
        var member = await db.Users.AsNoTracking()
            .Where(u => u.Id == request.Id && u.OrganizationId == organizationId)
            .ToDto()
            .FirstOrDefaultAsync(cancellationToken);

        return member is null ? IdentityErrors.MemberNotFound : member;
    }
}
