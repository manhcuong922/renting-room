using FluentValidation;
using Mediator;
using renting_room.Domain.Common;
using renting_room.Domain.Renters;

namespace renting_room.Application.Renters;

public sealed record GetRenterQuery(Guid Id) : IRequest<Result<RenterDto>>;

public sealed class GetRenterHandler(IRenterQueries renters) : IRequestHandler<GetRenterQuery, Result<RenterDto>>
{
    public async ValueTask<Result<RenterDto>> Handle(GetRenterQuery request, CancellationToken cancellationToken)
    {
        var renter = await renters.GetAsync(request.Id, cancellationToken);
        return renter is null ? RenterErrors.RenterNotFound : renter;
    }
}
