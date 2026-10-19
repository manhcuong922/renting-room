using Mediator;
using renting_room.Application.Common.Interfaces;
using renting_room.Application.Identity.Organizations;

namespace renting_room.Application.Renters;

public sealed record GetDataRetentionQuery : IRequest<DataRetentionDto>;

public sealed class GetDataRetentionHandler(IOrganizationQueries organizations, ICurrentUser currentUser)
    : IRequestHandler<GetDataRetentionQuery, DataRetentionDto>
{
    public async ValueTask<DataRetentionDto> Handle(GetDataRetentionQuery request, CancellationToken cancellationToken)
    {
        var organization = (await organizations.GetAsync(currentUser.OrganizationId!.Value, cancellationToken))!;
        return DataRetentionMapping.ToDto(organization);
    }
}
