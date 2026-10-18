using Mediator;
using Microsoft.EntityFrameworkCore;
using renting_room.Application.Common.Interfaces;
using renting_room.Domain.Common;
using renting_room.Domain.Contracts;

namespace renting_room.Application.Contracts;

public sealed record ChangeContractTemplateStateCommand(Guid Id, bool Archive) : IRequest<Result>;

public sealed class ChangeContractTemplateStateHandler(IAppDbContext db, TimeProvider clock) : IRequestHandler<ChangeContractTemplateStateCommand, Result>
{
    public async ValueTask<Result> Handle(ChangeContractTemplateStateCommand request, CancellationToken cancellationToken)
    {
        var template = await db.ContractTemplates.FirstOrDefaultAsync(t => t.Id == request.Id, cancellationToken);
        if (template is null)
            return Result.Failure(ContractTemplateErrors.NotFound);

        var result = request.Archive ? template.Archive(clock.GetUtcNow()) : template.Restore();
        if (result.IsFailure)
            return result;

        await db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
