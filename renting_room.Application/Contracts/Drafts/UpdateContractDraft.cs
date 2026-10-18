using FluentValidation;
using Mediator;
using Microsoft.EntityFrameworkCore;
using renting_room.Application.Common.Interfaces;
using renting_room.Domain.Common;

namespace renting_room.Application.Contracts;

public sealed record UpdateContractDraftCommand(Guid Id, ContractInput Contract, uint Version) : IRequest<Result>;

public sealed class UpdateContractDraftCommandValidator : AbstractValidator<UpdateContractDraftCommand>
{
    public UpdateContractDraftCommandValidator(TimeProvider clock) =>
        RuleFor(x => x.Contract).NotNull().SetValidator(new ContractInputValidator(clock));
}

public sealed class UpdateContractDraftHandler(IAppDbContext db) : IRequestHandler<UpdateContractDraftCommand, Result>
{
    public ValueTask<Result> Handle(UpdateContractDraftCommand request, CancellationToken cancellationToken) =>
        new(ContractMutation.RunAsync(db, request.Id, async contract =>
        {
            db.SetExpectedVersion(contract, request.Version);
            var room = await db.Rooms.AsNoTracking().FirstAsync(r => r.Id == contract.RoomId, cancellationToken);
            var property = await db.Properties.AsNoTracking().FirstAsync(p => p.Id == contract.PropertyId, cancellationToken);
            var data = await ContractDraftBuilder.BuildAsync(db, request.Contract, room, property, contract.TemplateId, cancellationToken);
            return data.IsFailure ? Result.Failure(data.Error!) : contract.UpdateDraft(data.Value!);
        }, cancellationToken));
}
