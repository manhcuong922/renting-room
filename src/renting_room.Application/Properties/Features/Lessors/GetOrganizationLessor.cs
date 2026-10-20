using Mediator;
using renting_room.Application.Common.Interfaces;
using renting_room.Domain.Common;
using renting_room.Application.Identity.Organizations;

namespace renting_room.Application.Properties;

public sealed record GetOrganizationLessorQuery : IRequest<OrganizationLessorDto>;

public sealed class GetOrganizationLessorHandler(IOrganizationRepository organizations, ICurrentUser currentUser, TimeProvider clock)
    : IRequestHandler<GetOrganizationLessorQuery, OrganizationLessorDto>
{
    public async ValueTask<OrganizationLessorDto> Handle(GetOrganizationLessorQuery request, CancellationToken cancellationToken)
    {
        var organization = await organizations.GetAsync(currentUser.OrganizationId!.Value, cancellationToken)
            ?? throw new InvalidOperationException("Organization of the current user not found.");
        return organization.ToLessorDto(clock.GetUtcNow().ToBusinessDate());
    }
}
