using FluentValidation;
using Mediator;
using renting_room.Application.Common.Interfaces;
using renting_room.Application.Common.Validation;
using renting_room.Domain.Common;

namespace renting_room.Application.Contracts;

public sealed record UpdateContractNoteCommand(Guid ContractId, string? Note) : IRequest<Result>;

public sealed class UpdateContractNoteCommandValidator : AbstractValidator<UpdateContractNoteCommand>
{
    public UpdateContractNoteCommandValidator() => RuleFor(x => x.Note).OptionalText(2000);
}

public sealed class UpdateContractNoteHandler(IAppDbContext db) : IRequestHandler<UpdateContractNoteCommand, Result>
{
    public ValueTask<Result> Handle(UpdateContractNoteCommand request, CancellationToken cancellationToken) =>
        new(ContractMutation.RunAsync(db, request.ContractId,
            contract => Task.FromResult(contract.UpdateNote(request.Note)), cancellationToken));
}
