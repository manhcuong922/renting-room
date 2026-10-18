using Mediator;
using Microsoft.EntityFrameworkCore;
using renting_room.Application.Common.Interfaces;
using renting_room.Domain.Common;
using renting_room.Domain.Properties;

namespace renting_room.Application.Properties;

public sealed record GetPropertyQuery(Guid Id) : IRequest<Result<PropertyDetailDto>>;

public sealed class GetPropertyHandler(IAppDbContext db, TimeProvider clock) : IRequestHandler<GetPropertyQuery, Result<PropertyDetailDto>>
{
    public async ValueTask<Result<PropertyDetailDto>> Handle(GetPropertyQuery request, CancellationToken cancellationToken)
    {
        var property = await db.Properties.AsNoTracking().FirstOrDefaultAsync(p => p.Id == request.Id, cancellationToken);
        return property is null
            ? PropertyErrors.PropertyNotFound
            : await property.ToDetailAsync(db, clock.GetUtcNow().ToBusinessDate(), cancellationToken);
    }
}
