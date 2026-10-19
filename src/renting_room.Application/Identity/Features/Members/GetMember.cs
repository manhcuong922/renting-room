using Mediator;
using renting_room.Application.Common.Interfaces;
using renting_room.Domain.Common;
using renting_room.Domain.Identity;

namespace renting_room.Application.Identity.Members;

public sealed record GetMemberQuery(Guid Id) : IRequest<Result<MemberDto>>;

public sealed class GetMemberHandler(IUserQueries users, ICurrentUser currentUser) : IRequestHandler<GetMemberQuery, Result<MemberDto>>
{
    public async ValueTask<Result<MemberDto>> Handle(GetMemberQuery request, CancellationToken cancellationToken)
    {
        var member = await users.GetMemberAsync(request.Id, currentUser.OrganizationId, cancellationToken);

        return member is null ? IdentityErrors.MemberNotFound : member;
    }
}
