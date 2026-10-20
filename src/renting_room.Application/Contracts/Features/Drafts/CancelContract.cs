using FluentValidation;
using Mediator;
using renting_room.Application.Common.Validation;
using renting_room.Domain.Common;

namespace renting_room.Application.Contracts;

public sealed record CancelContractCommand(Guid Id, string Reason) : IRequest<Result<IReadOnlyList<Warning>>>;

public sealed class CancelContractCommandValidator : AbstractValidator<CancelContractCommand>
{
    public CancelContractCommandValidator() => RuleFor(x => x.Reason).RequiredText(500, "Lý do hủy");
}

public sealed class CancelContractHandler(ContractMutation mutation, IContractRepository contracts, ResignVehicleRestorer vehicles, TimeProvider clock)
    : IRequestHandler<CancelContractCommand, Result<IReadOnlyList<Warning>>>
{
    public ValueTask<Result<IReadOnlyList<Warning>>> Handle(CancelContractCommand request, CancellationToken cancellationToken) =>
        new(mutation.RunAsync<IReadOnlyList<Warning>>(request.Id, async contract =>
        {
            var cancelled = contract.Cancel(request.Reason, clock.GetUtcNow());
            if (cancelled.IsFailure)
                return cancelled.Error!;
            if (contract.PreviousContractId is not { } previousId
                || await contracts.GetForUpdateAsync(previousId, cancellationToken) is not { } previous)
                return Result.Success<IReadOnlyList<Warning>>([]);

            previous.RevertDepositTransfer(contract.ContractNo);
            return Result.Success(await vehicles.RestoreAsync(previous, contract.Id, cancellationToken));
        }, cancellationToken));
}
