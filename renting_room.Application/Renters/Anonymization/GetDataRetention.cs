using Mediator;
using Microsoft.EntityFrameworkCore;
using renting_room.Application.Common.Interfaces;

namespace renting_room.Application.Renters;

public sealed record GetDataRetentionQuery : IRequest<DataRetentionDto>;

public sealed class GetDataRetentionHandler(IAppDbContext db, ICurrentUser currentUser) : IRequestHandler<GetDataRetentionQuery, DataRetentionDto>
{
    public async ValueTask<DataRetentionDto> Handle(GetDataRetentionQuery request, CancellationToken cancellationToken)
    {
        var organization = await db.Organizations.AsNoTracking().FirstAsync(o => o.Id == currentUser.OrganizationId, cancellationToken);
        return DataRetentionMapping.ToDto(organization);
    }
}
