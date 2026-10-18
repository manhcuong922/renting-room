using FluentValidation;
using Mediator;
using Microsoft.EntityFrameworkCore;
using renting_room.Application.Common.Interfaces;
using renting_room.Domain.Common;
using renting_room.Domain.Renters;

namespace renting_room.Application.Renters;

public sealed record ListRenterConsentsQuery(Guid RenterId) : IRequest<Result<IReadOnlyList<RenterConsentDto>>>;

public sealed class ListRenterConsentsHandler(IAppDbContext db) : IRequestHandler<ListRenterConsentsQuery, Result<IReadOnlyList<RenterConsentDto>>>
{
    public async ValueTask<Result<IReadOnlyList<RenterConsentDto>>> Handle(ListRenterConsentsQuery request, CancellationToken cancellationToken)
    {
        if (!await db.Renters.AnyAsync(r => r.Id == request.RenterId, cancellationToken))
            return RenterErrors.RenterNotFound;
        var consents = await db.RenterConsents.AsNoTracking().Where(c => c.RenterId == request.RenterId)
            .OrderByDescending(c => c.GivenAt).ThenByDescending(c => c.CreatedAt).ToListAsync(cancellationToken);
        return consents.Select(c => c.ToDto()).ToList();
    }
}
