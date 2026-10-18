using Mediator;
using Microsoft.EntityFrameworkCore;
using renting_room.Application.Common.Interfaces;
using renting_room.Domain.Common;
using renting_room.Domain.Properties;

namespace renting_room.Application.Properties;

public sealed record RestorePropertyCommand(Guid Id) : IRequest<Result>;

public sealed class RestorePropertyHandler(IAppDbContext db) : IRequestHandler<RestorePropertyCommand, Result>
{
    public async ValueTask<Result> Handle(RestorePropertyCommand request, CancellationToken cancellationToken)
    {
        var property = await db.Properties.FirstOrDefaultAsync(p => p.Id == request.Id, cancellationToken);
        if (property is null)
            return Result.Failure(PropertyErrors.PropertyNotFound);

        var restored = property.Restore();
        if (restored.IsFailure)
            return restored;

        await db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
