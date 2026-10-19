using Mediator;
using renting_room.Domain.Common;
using renting_room.Domain.Fees;

namespace renting_room.Application.Fees;

public sealed record GetFeeTypeQuery(Guid Id) : IRequest<Result<FeeTypeDto>>;

public sealed class GetFeeTypeHandler(IFeeQueries queries, TimeProvider clock) : IRequestHandler<GetFeeTypeQuery, Result<FeeTypeDto>>
{
    public async ValueTask<Result<FeeTypeDto>> Handle(GetFeeTypeQuery request, CancellationToken cancellationToken)
    {
        var fee = await queries.GetAsync(request.Id, clock.GetUtcNow().ToBusinessDate(), cancellationToken);
        return fee is null ? FeeErrors.NotFound : fee;
    }
}
