using FluentValidation;
using Mediator;
using Microsoft.EntityFrameworkCore;
using renting_room.Application.Common.Interfaces;
using renting_room.Domain.Common;
using renting_room.Domain.Renters;

namespace renting_room.Application.Renters;

// Queries
public sealed record GetRenterQuery(Guid Id) : IRequest<Result<RenterDto>>;

public sealed class GetRenterHandler(IAppDbContext db) : IRequestHandler<GetRenterQuery, Result<RenterDto>>
{
    public async ValueTask<Result<RenterDto>> Handle(GetRenterQuery request, CancellationToken cancellationToken)
    {
        var renter = await db.Renters.AsNoTracking().Where(r => r.Id == request.Id).ToDto().FirstOrDefaultAsync(cancellationToken);
        return renter is null ? RenterErrors.RenterNotFound : renter;
    }
}
