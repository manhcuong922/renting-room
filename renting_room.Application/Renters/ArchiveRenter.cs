using Mediator;
using Microsoft.EntityFrameworkCore;
using renting_room.Application.Common.Interfaces;
using renting_room.Domain.Common;
using renting_room.Domain.Renters;

namespace renting_room.Application.Renters;

// Queries
public sealed record ArchiveRenterCommand(Guid Id, bool Archived) : IRequest<Result>;

/// <summary>RT-BR-05: ẩn / hiện lại hồ sơ. Người còn HĐ nháp / hiệu lực / thanh lý thì không ẩn được.</summary>
public sealed class ArchiveRenterHandler(IAppDbContext db, TimeProvider clock) : IRequestHandler<ArchiveRenterCommand, Result>
{
    public async ValueTask<Result> Handle(ArchiveRenterCommand request, CancellationToken cancellationToken)
    {
        var renter = await db.Renters.FirstOrDefaultAsync(r => r.Id == request.Id, cancellationToken);
        if (renter is null)
            return Result.Failure(RenterErrors.RenterNotFound);
        if (request.Archived && await RenterAnonymizer.HasOpenContractAsync(db, request.Id, cancellationToken))
            return Result.Failure(RenterErrors.HasOpenContract);

        if (request.Archived)
            renter.Archive(clock.GetUtcNow());
        else
            renter.Restore();
        await db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
