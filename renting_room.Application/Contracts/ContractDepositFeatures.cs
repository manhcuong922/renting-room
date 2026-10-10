using FluentValidation;
using Mediator;
using renting_room.Application.Common.Interfaces;
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

/// <summary>M08 PM-BR-33: "Đã hoàn trả cọc" — cọc chỉ để theo dõi, không ghi tiền vào / ra, không trừ vào phiếu.</summary>
public sealed class RefundDepositHandler(IAppDbContext db, TimeProvider clock) : IRequestHandler<RefundDepositCommand, Result>
{
    public ValueTask<Result> Handle(RefundDepositCommand request, CancellationToken cancellationToken) =>
        new(ContractMutation.RunAsync(db, request.ContractId, contract => Task.FromResult(
            contract.RefundDeposit(request.RefundedOn, request.Amount, request.Note, clock.GetUtcNow().ToBusinessDate())), cancellationToken));
}

public sealed record CancelDepositRefundCommand(Guid ContractId) : IRequest<Result>;

/// <summary>Bỏ đánh dấu hoàn trả / chuyển cọc (nhập nhầm) ⇒ "Đang giữ cọc".</summary>
public sealed class CancelDepositRefundHandler(IAppDbContext db) : IRequestHandler<CancelDepositRefundCommand, Result>
{
    public ValueTask<Result> Handle(CancelDepositRefundCommand request, CancellationToken cancellationToken) =>
        new(ContractMutation.RunAsync(db, request.ContractId, contract => Task.FromResult(contract.CancelDepositRefund()), cancellationToken));
}
