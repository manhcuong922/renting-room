using Mediator;
using renting_room.Application.Common.Interfaces;

namespace renting_room.Application.Identity.Members;

public sealed record ListMembersQuery(bool IncludeRemoved = false) : IRequest<IReadOnlyList<MemberDto>>;

public sealed class ListMembersHandler(IUserQueries users, ICurrentUser currentUser)
    : IRequestHandler<ListMembersQuery, IReadOnlyList<MemberDto>>
{
    public async ValueTask<IReadOnlyList<MemberDto>> Handle(ListMembersQuery request, CancellationToken cancellationToken) =>
        await users.ListMembersAsync(currentUser.OrganizationId, request.IncludeRemoved, cancellationToken);
}
