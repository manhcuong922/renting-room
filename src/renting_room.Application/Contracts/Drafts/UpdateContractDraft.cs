using FluentValidation;
using Mediator;
using renting_room.Application.Properties;
using renting_room.Application.Rooms;
using renting_room.Application.Common.Interfaces;
using renting_room.Domain.Common;

namespace renting_room.Application.Contracts;

public sealed record UpdateContractDraftCommand(Guid Id, ContractInput Contract, uint Version) : IRequest<Result>;

public sealed class UpdateContractDraftCommandValidator : AbstractValidator<UpdateContractDraftCommand>
{
    public UpdateContractDraftCommandValidator(TimeProvider clock) =>
        RuleFor(x => x.Contract).NotNull().SetValidator(new ContractInputValidator(clock));
}

public sealed class UpdateContractDraftHandler(
    ContractMutation mutation, IUnitOfWork unitOfWork, IRoomQueries rooms, IPropertyQueries properties, ContractDraftBuilder builder)
    : IRequestHandler<UpdateContractDraftCommand, Result>
{
    public ValueTask<Result> Handle(UpdateContractDraftCommand request, CancellationToken cancellationToken) =>
        new(mutation.RunAsync(request.Id, async contract =>
        {
            unitOfWork.SetExpectedVersion(contract, request.Version);
            var room = (await rooms.FindAsync(contract.RoomId, cancellationToken))!;
            var property = (await properties.GetAsync(contract.PropertyId, cancellationToken))!;
            var data = await builder.BuildAsync(request.Contract, room, property, contract.TemplateId, cancellationToken);
            return data.IsFailure ? Result.Failure(data.Error!) : contract.UpdateDraft(data.Value!);
        }, cancellationToken));
}
