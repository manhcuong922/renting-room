using FluentValidation;
using Mediator;
using renting_room.Application.Common.Validation;
using renting_room.Domain.Common;
using renting_room.Domain.Contracts;

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

public sealed class RefundDepositHandler(ContractMutation mutation, FinalDraftRefresher finalDraft, TimeProvider clock) : IRequestHandler<RefundDepositCommand, Result>
{
    public ValueTask<Result> Handle(RefundDepositCommand request, CancellationToken cancellationToken) =>
        new(DepositChange.RunAsync(mutation, finalDraft, request.ContractId,
            contract => contract.RefundDeposit(request.RefundedOn, request.Amount, request.Note, clock.GetUtcNow().ToBusinessDate()), cancellationToken));
}

public sealed record ForfeitDepositCommand(Guid ContractId, DateOnly Date, string Note) : IRequest<Result>;

public sealed class ForfeitDepositCommandValidator : AbstractValidator<ForfeitDepositCommand>
{
    public ForfeitDepositCommandValidator() => RuleFor(x => x.Note).RequiredText(500, "Lý do không trả cọc");
}

public sealed class ForfeitDepositHandler(ContractMutation mutation, FinalDraftRefresher finalDraft, TimeProvider clock) : IRequestHandler<ForfeitDepositCommand, Result>
{
    public ValueTask<Result> Handle(ForfeitDepositCommand request, CancellationToken cancellationToken) =>
        new(DepositChange.RunAsync(mutation, finalDraft, request.ContractId,
            contract => contract.ForfeitDeposit(request.Date, request.Note, clock.GetUtcNow().ToBusinessDate()), cancellationToken));
}

public sealed record CancelDepositRefundCommand(Guid ContractId) : IRequest<Result>;

public sealed class CancelDepositRefundHandler(ContractMutation mutation, FinalDraftRefresher finalDraft) : IRequestHandler<CancelDepositRefundCommand, Result>
{
    public ValueTask<Result> Handle(CancelDepositRefundCommand request, CancellationToken cancellationToken) =>
        new(DepositChange.RunAsync(mutation, finalDraft, request.ContractId, contract => contract.CancelDepositRefund(), cancellationToken));
}

/// <summary>Đổi trạng thái cọc rồi tính lại nháp hóa đơn trả phòng (dòng "Trừ tiền cọc" — M07 BL-BR-41).</summary>
internal static class DepositChange
{
    public static Task<Result> RunAsync(
        ContractMutation mutation, FinalDraftRefresher finalDraft, Guid contractId, Func<Contract, Result> change, CancellationToken ct) =>
        mutation.RunAsync(contractId, async contract =>
        {
            var changed = change(contract);
            if (changed.IsSuccess)
                await finalDraft.RefreshAsync(contract, ct);
            return changed;
        }, ct);
}
