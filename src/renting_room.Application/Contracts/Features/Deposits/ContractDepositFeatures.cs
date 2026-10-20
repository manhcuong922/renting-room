using FluentValidation;
using Mediator;
using renting_room.Application.Common.Validation;
using renting_room.Domain.Common;

namespace renting_room.Application.Contracts;

/// <param name="Amount">Số tiền thực trả lại — null = đủ cọc; ít hơn cọc (giữ lại một phần / mất cọc) thì <paramref name="Note"/> bắt buộc.</param>
public sealed record RefundDepositCommand(Guid ContractId, DateOnly RefundedOn, decimal? Amount, string? Note) : IRequest<Result>;

public sealed class RefundDepositCommandValidator : AbstractValidator<RefundDepositCommand>
{
    public RefundDepositCommandValidator()
    {
        RuleFor(x => x.Amount).OptionalMoney();
        RuleFor(x => x.Note).OptionalText(500);
    }
}

public sealed class RefundDepositHandler(ContractMutation mutation, TimeProvider clock) : IRequestHandler<RefundDepositCommand, Result>
{
    public ValueTask<Result> Handle(RefundDepositCommand request, CancellationToken cancellationToken) =>
        new(mutation.RunAsync(request.ContractId, contract => Task.FromResult(
            contract.RefundDeposit(request.RefundedOn, request.Amount, request.Note, clock.GetUtcNow().ToBusinessDate())), cancellationToken));
}

public sealed record ForfeitDepositCommand(Guid ContractId, DateOnly Date, string Note) : IRequest<Result>;

public sealed class ForfeitDepositCommandValidator : AbstractValidator<ForfeitDepositCommand>
{
    public ForfeitDepositCommandValidator() => RuleFor(x => x.Note).RequiredText(500, "Lý do không trả cọc");
}

public sealed class ForfeitDepositHandler(ContractMutation mutation, TimeProvider clock) : IRequestHandler<ForfeitDepositCommand, Result>
{
    public ValueTask<Result> Handle(ForfeitDepositCommand request, CancellationToken cancellationToken) =>
        new(mutation.RunAsync(request.ContractId, contract => Task.FromResult(
            contract.ForfeitDeposit(request.Date, request.Note, clock.GetUtcNow().ToBusinessDate())), cancellationToken));
}

public sealed record CancelDepositRefundCommand(Guid ContractId) : IRequest<Result>;

public sealed class CancelDepositRefundHandler(ContractMutation mutation) : IRequestHandler<CancelDepositRefundCommand, Result>
{
    public ValueTask<Result> Handle(CancelDepositRefundCommand request, CancellationToken cancellationToken) =>
        new(mutation.RunAsync(request.ContractId, contract => Task.FromResult(contract.CancelDepositRefund()), cancellationToken));
}
