using FluentValidation;
using Mediator;
using Microsoft.EntityFrameworkCore;
using renting_room.Application.Common.Interfaces;
using renting_room.Domain.Common;
using renting_room.Domain.Properties;

namespace renting_room.Application.Fees;

public sealed record ListFeeTypesQuery(Guid PropertyId, bool IncludeArchived) : IRequest<Result<IReadOnlyList<FeeTypeDto>>>;

public sealed class ListFeeTypesHandler(IAppDbContext db, TimeProvider clock) : IRequestHandler<ListFeeTypesQuery, Result<IReadOnlyList<FeeTypeDto>>>
{
    public async ValueTask<Result<IReadOnlyList<FeeTypeDto>>> Handle(ListFeeTypesQuery request, CancellationToken cancellationToken)
    {
        if (!await db.Properties.AnyAsync(p => p.Id == request.PropertyId, cancellationToken))
            return PropertyErrors.PropertyNotFound;

        var fees = await db.FeeTypes.AsNoTracking().Include(f => f.Prices)
            .Where(f => f.PropertyId == request.PropertyId && (request.IncludeArchived || f.ArchivedAt == null))
            .OrderBy(f => f.SortOrder).ThenBy(f => f.Name)
            .ToListAsync(cancellationToken);
        return fees.Select(f => FeeTypeDto.From(f, clock.GetUtcNow().ToBusinessDate())).ToList();
    }
}
