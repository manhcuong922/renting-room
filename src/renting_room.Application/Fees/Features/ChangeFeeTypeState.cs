using Mediator;
using renting_room.Application.Common.Interfaces;
using renting_room.Domain.Common;
using renting_room.Domain.Fees;

namespace renting_room.Application.Fees;

public sealed record ChangeFeeTypeStateCommand(Guid Id, bool Archive) : IRequest<Result>;

public sealed class ChangeFeeTypeStateHandler(IFeeTypeRepository fees, IFeeQueries queries, IUnitOfWork unitOfWork, TimeProvider clock)
    : IRequestHandler<ChangeFeeTypeStateCommand, Result>
{
    public async ValueTask<Result> Handle(ChangeFeeTypeStateCommand request, CancellationToken cancellationToken)
    {
        var fee = await fees.GetAsync(request.Id, cancellationToken);
        if (fee is null)
            return Result.Failure(FeeErrors.NotFound);

        if (request.Archive)
        {
            var today = clock.GetUtcNow().ToBusinessDate();
            if (await queries.IsUsedByOpenContractsAsync(fee.Id, today, cancellationToken))
                return Result.Failure(FeeErrors.InUse);
            if (fee.Group == FeeGroup.Metered && await queries.HasActiveMetersAsync(fee.Id, cancellationToken))
                return Result.Failure(FeeErrors.HasActiveMeters);
            var archived = fee.Archive(clock.GetUtcNow());
            if (archived.IsFailure)
                return archived;
        }
        else
        {
            if (await fees.IsNameTakenAsync(fee.PropertyId, fee.NameNormalized, fee.SystemCode, excludeId: null, cancellationToken))
                return Result.Failure(FeeErrors.NameTaken);
            var restored = fee.Restore();
            if (restored.IsFailure)
                return restored;
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
