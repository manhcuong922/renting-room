using FluentValidation;
using Mediator;
using renting_room.Application.Billing;
using renting_room.Application.Common.Interfaces;
using renting_room.Domain.Billing;
using renting_room.Domain.Common;
using renting_room.Domain.Contracts;

namespace renting_room.Application.Contracts;

/// <summary>M05 CT-BR-11: đổi kỳ cuối (trọn kỳ / theo ngày / không thu) khi hóa đơn trả phòng chưa chốt — nháp tự tính lại, giữ ô sửa tay.</summary>
public sealed record ChangeFinalPeriodChargeCommand(Guid ContractId, FinalPeriodCharge? FinalPeriodCharge) : IRequest<Result>;

public sealed class ChangeFinalPeriodChargeCommandValidator : AbstractValidator<ChangeFinalPeriodChargeCommand>
{
    public ChangeFinalPeriodChargeCommandValidator() =>
        RuleFor(x => x.FinalPeriodCharge).IsInEnum().When(x => x.FinalPeriodCharge is not null);
}

public sealed class ChangeFinalPeriodChargeHandler(
    ContractMutation mutation, IInvoiceRepository invoices, DraftRecalculator recalculator, IUnitOfWork unitOfWork)
    : IRequestHandler<ChangeFinalPeriodChargeCommand, Result>
{
    public ValueTask<Result> Handle(ChangeFinalPeriodChargeCommand request, CancellationToken cancellationToken) =>
        new(mutation.RunAsync(request.ContractId, async contract =>
        {
            var final = await invoices.FindFinalForUpdateAsync(contract.Id, cancellationToken);
            if (final?.Status == InvoiceStatus.Finalized)
                return Result.Failure(BillingErrors.FinalFinalized);
            var changed = contract.ChangeFinalPeriodCharge(request.FinalPeriodCharge);
            if (changed.IsFailure || final is null)
                return changed;
            await unitOfWork.LockForUpdateAsync<Invoice>(final.Id, cancellationToken);
            await recalculator.RecalculateAsync(contract.Id, final.Id, keepManualEdits: true, cancellationToken);
            return Result.Success();
        }, cancellationToken));
}
