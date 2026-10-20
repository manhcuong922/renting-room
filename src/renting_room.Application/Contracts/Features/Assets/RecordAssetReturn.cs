using FluentValidation;
using Mediator;
using renting_room.Application.Common.Validation;
using renting_room.Domain.Common;

namespace renting_room.Application.Contracts;

public sealed record RecordAssetReturnCommand(Guid ContractId, Guid AssetId, string? ConditionAtReturn) : IRequest<Result>;

public sealed class RecordAssetReturnCommandValidator : AbstractValidator<RecordAssetReturnCommand>
{
    public RecordAssetReturnCommandValidator()
    {
        RuleFor(x => x.ConditionAtReturn).OptionalText(500);
    }
}

public sealed class RecordAssetReturnHandler(ContractMutation mutation) : IRequestHandler<RecordAssetReturnCommand, Result>
{
    public ValueTask<Result> Handle(RecordAssetReturnCommand request, CancellationToken cancellationToken) =>
        new(mutation.RunAsync(request.ContractId, contract => Task.FromResult(
            contract.RecordAssetReturn(request.AssetId, request.ConditionAtReturn)), cancellationToken));
}
