using Mediator;
using renting_room.Application.Common.Interfaces;
using renting_room.Domain.Common;
using renting_room.Domain.Renters;

namespace renting_room.Application.Renters;

public sealed record ArchiveRenterCommand(Guid Id, bool Archived) : IRequest<Result>;

public sealed class ArchiveRenterHandler(IRenterRepository renters, IRenterQueries renterQueries, IUnitOfWork unitOfWork, TimeProvider clock)
    : IRequestHandler<ArchiveRenterCommand, Result>
{
    public async ValueTask<Result> Handle(ArchiveRenterCommand request, CancellationToken cancellationToken)
    {
        var renter = await renters.GetForUpdateAsync(request.Id, cancellationToken);
        if (renter is null)
            return Result.Failure(RenterErrors.RenterNotFound);
        if (request.Archived && await renterQueries.HasOpenContractAsync(request.Id, cancellationToken))
            return Result.Failure(RenterErrors.HasOpenContract);

        if (request.Archived)
            renter.Archive(clock.GetUtcNow());
        else
            renter.Restore();
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
