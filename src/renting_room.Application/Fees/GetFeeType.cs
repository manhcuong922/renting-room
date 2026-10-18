using Mediator;
using Microsoft.EntityFrameworkCore;
using renting_room.Application.Common.Interfaces;
using renting_room.Domain.Common;
using renting_room.Domain.Fees;

namespace renting_room.Application.Fees;

public sealed record GetFeeTypeQuery(Guid Id) : IRequest<Result<FeeTypeDto>>;

public sealed class GetFeeTypeHandler(IAppDbContext db, TimeProvider clock) : IRequestHandler<GetFeeTypeQuery, Result<FeeTypeDto>>
{
    public async ValueTask<Result<FeeTypeDto>> Handle(GetFeeTypeQuery request, CancellationToken cancellationToken)
    {
        var fee = await db.FeeTypes.AsNoTracking().Include(f => f.Prices).FirstOrDefaultAsync(f => f.Id == request.Id, cancellationToken);
        return fee is null ? FeeErrors.NotFound : FeeTypeDto.From(fee, clock.GetUtcNow().ToBusinessDate());
    }
}
