using FluentValidation;
using Mediator;
using renting_room.Application.Common.Validation;
using renting_room.Domain.Common;

namespace renting_room.Application.Contracts;

public sealed record StartHoldoverCommand(Guid ContractId, string? Note) : IRequest<Result>;

public sealed class StartHoldoverCommandValidator : AbstractValidator<StartHoldoverCommand>
{
    public StartHoldoverCommandValidator() => RuleFor(x => x.Note).OptionalText(500);
}

public sealed class StartHoldoverHandler(ContractMutation mutation, TimeProvider clock) : IRequestHandler<StartHoldoverCommand, Result>
{
    public ValueTask<Result> Handle(StartHoldoverCommand request, CancellationToken cancellationToken) =>
        new(mutation.RunAsync(request.ContractId,
            contract => Task.FromResult(contract.StartHoldover(clock.GetUtcNow().ToBusinessDate(), request.Note)), cancellationToken));
}
