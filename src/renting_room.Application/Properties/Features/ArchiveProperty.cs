using Mediator;
using renting_room.Application.Common.Interfaces;
using renting_room.Domain.Common;
using renting_room.Domain.Properties;
using renting_room.Application.Rooms;

namespace renting_room.Application.Properties;

public sealed record ArchivePropertyCommand(Guid Id) : IRequest<Result>;

public sealed class ArchivePropertyHandler(
    IPropertyRepository properties, IPropertyQueries queries, IRoomRepository rooms, IUnitOfWork unitOfWork, TimeProvider clock)
    : IRequestHandler<ArchivePropertyCommand, Result>
{
    public async ValueTask<Result> Handle(ArchivePropertyCommand request, CancellationToken cancellationToken)
    {
        await using var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken);

        var property = await properties.GetForUpdateAsync(request.Id, cancellationToken);
        if (property is null)
            return Result.Failure(PropertyErrors.PropertyNotFound);

        var today = clock.GetUtcNow().ToBusinessDate();
        if (await queries.HasOpenContractsAsync(property.Id, today, cancellationToken))
            return Result.Failure(PropertyErrors.PropertyHasActiveContracts);

        var now = clock.GetUtcNow();
        var archived = property.Archive(now);
        if (archived.IsFailure)
            return archived;

        foreach (var room in await rooms.ListActiveByPropertyAsync(property.Id, cancellationToken))
            room.Archive(now);

        await unitOfWork.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return Result.Success();
    }
}
