using Mediator;
using renting_room.Application.Common.Interfaces;
using renting_room.Domain.Common;
using renting_room.Domain.Fees;

namespace renting_room.Application.Fees;

public sealed record DeleteFeePriceCommand(Guid FeeTypeId, Guid PriceId) : IRequest<Result>;

public sealed class DeleteFeePriceHandler(IFeeTypeRepository fees, IUnitOfWork unitOfWork, IFeePriceLockReader locks)
    : IRequestHandler<DeleteFeePriceCommand, Result>
{
    public async ValueTask<Result> Handle(DeleteFeePriceCommand request, CancellationToken cancellationToken)
    {
        await using var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken);
        var fee = await fees.GetForUpdateAsync(request.FeeTypeId, cancellationToken);
        if (fee is null)
            return Result.Failure(FeeErrors.NotFound);

        var removed = fee.RemovePrice(request.PriceId, await locks.GetLockedUntilAsync(fee.Id, cancellationToken));
        if (removed.IsFailure)
            return removed;

        await unitOfWork.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return Result.Success();
    }
}
