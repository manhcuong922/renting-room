using Mediator;
using renting_room.Application.Properties;
using renting_room.Domain.Common;
using renting_room.Domain.Properties;

namespace renting_room.Application.Fees;

public sealed record ListFeeTypesQuery(Guid PropertyId, bool IncludeArchived) : IRequest<Result<IReadOnlyList<FeeTypeDto>>>;

public sealed class ListFeeTypesHandler(IPropertyQueries properties, IFeeQueries queries, TimeProvider clock)
    : IRequestHandler<ListFeeTypesQuery, Result<IReadOnlyList<FeeTypeDto>>>
{
    public async ValueTask<Result<IReadOnlyList<FeeTypeDto>>> Handle(ListFeeTypesQuery request, CancellationToken cancellationToken)
    {
        if (!await properties.ExistsAsync(request.PropertyId, cancellationToken))
            return PropertyErrors.PropertyNotFound;

        var fees = await queries.ListAsync(request.PropertyId, request.IncludeArchived, clock.GetUtcNow().ToBusinessDate(), cancellationToken);
        return Result.Success(fees);
    }
}
