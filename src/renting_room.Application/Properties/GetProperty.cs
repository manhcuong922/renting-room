using Mediator;
using renting_room.Application.Common.Interfaces;
using renting_room.Domain.Common;
using renting_room.Domain.Properties;
using renting_room.Application.Identity.Organizations;

namespace renting_room.Application.Properties;

public sealed record GetPropertyQuery(Guid Id) : IRequest<Result<PropertyDetailDto>>;

public sealed class GetPropertyHandler(IPropertyQueries properties, IOrganizationQueries organizations, TimeProvider clock)
    : IRequestHandler<GetPropertyQuery, Result<PropertyDetailDto>>
{
    public async ValueTask<Result<PropertyDetailDto>> Handle(GetPropertyQuery request, CancellationToken cancellationToken)
    {
        var property = await properties.GetAsync(request.Id, cancellationToken);
        return property is null
            ? PropertyErrors.PropertyNotFound
            : await property.ToDetailAsync(organizations, clock.GetUtcNow().ToBusinessDate(), cancellationToken);
    }
}
