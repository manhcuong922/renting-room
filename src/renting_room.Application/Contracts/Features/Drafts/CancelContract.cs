using FluentValidation;
using Mediator;
using renting_room.Application.Common.Validation;
using renting_room.Domain.Common;

namespace renting_room.Application.Contracts;

public sealed record CancelContractCommand(Guid Id, string Reason) : IRequest<Result>;

public sealed class CancelContractCommandValidator : AbstractValidator<CancelContractCommand>
{
    public CancelContractCommandValidator() => RuleFor(x => x.Reason).RequiredText(500, "Lý do hủy");
}

public sealed class CancelContractHandler(ContractMutation mutation, IContractRepository contracts, TimeProvider clock) : IRequestHandler<CancelContractCommand, Result>
{
    public ValueTask<Result> Handle(CancelContractCommand request, CancellationToken cancellationToken) =>
        new(mutation.RunAsync(request.Id, async contract =>
        {
            var cancelled = contract.Cancel(request.Reason, clock.GetUtcNow());
            if (cancelled.IsFailure)
                return cancelled;
            // PM-BR-34: hủy HĐ nháp ký lại ⇒ cọc của HĐ cũ (đã ghi "chuyển sang HĐ này") về "Đang giữ".
            if (contract.PreviousContractId is { } previousId
                && await contracts.FindForUpdateAsync(previousId, cancellationToken) is { } previous)
                previous.RevertDepositTransfer(contract.ContractNo);
            return Result.Success();
        }, cancellationToken));
}
