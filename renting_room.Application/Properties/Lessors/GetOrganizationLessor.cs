using Mediator;
using Microsoft.EntityFrameworkCore;
using renting_room.Application.Common.Interfaces;
using renting_room.Domain.Common;

namespace renting_room.Application.Properties;

public sealed record GetOrganizationLessorQuery : IRequest<OrganizationLessorDto>;

public sealed class GetOrganizationLessorHandler(IAppDbContext db, ICurrentUser currentUser, TimeProvider clock)
    : IRequestHandler<GetOrganizationLessorQuery, OrganizationLessorDto>
{
    public async ValueTask<OrganizationLessorDto> Handle(GetOrganizationLessorQuery request, CancellationToken cancellationToken)
    {
        var organization = await db.Organizations.AsNoTracking().FirstAsync(o => o.Id == currentUser.OrganizationId, cancellationToken);
        return organization.ToLessorDto(clock.GetUtcNow().ToBusinessDate());
    }
}
