using FluentValidation;
using Mediator;
using renting_room.Application.Common.Interfaces;
using renting_room.Application.Common.Validation;
using renting_room.Domain.Billing;
using renting_room.Domain.Common;
using renting_room.Domain.Contracts;

namespace renting_room.Application.Billing;

public sealed record VoidInvoiceCommand(Guid Id, string Reason) : IRequest<Result<InvoiceDetailDto>>;

public sealed class VoidInvoiceCommandValidator : AbstractValidator<VoidInvoiceCommand>
{
    public VoidInvoiceCommandValidator() => RuleFor(x => x.Reason).RequiredText(300, "Lý do hủy");
}

public sealed class VoidInvoiceHandler(
    IUnitOfWork unitOfWork, IInvoiceRepository invoices, IInvoiceQueries invoiceQueries, RoomChargeBilling roomCharges, IRoomChargeRepository charges,
    ReadingAdjustmentCharges adjustments, TimeProvider clock)
    : IRequestHandler<VoidInvoiceCommand, Result<InvoiceDetailDto>>
{
    public async ValueTask<Result<InvoiceDetailDto>> Handle(VoidInvoiceCommand request, CancellationToken cancellationToken)
    {
        var contractId = await invoiceQueries.GetContractIdAsync(request.Id, cancellationToken);
        if (contractId is null)
            return BillingErrors.NotFound;

        await using var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken);
        await unitOfWork.LockForUpdateAsync<Contract>(contractId.Value, cancellationToken);
        await unitOfWork.LockForUpdateAsync<Invoice>(request.Id, cancellationToken);
        var invoice = (await invoices.GetForUpdateAsync(request.Id, cancellationToken))!;
        if (invoice.Status == InvoiceStatus.Finalized && await invoiceQueries.HasLaterInvoiceAsync(invoice, cancellationToken))
            return BillingErrors.NotLatest;

        var now = clock.GetUtcNow();
        var voided = invoice.Void(request.Reason, now);
        if (voided.IsFailure)
            return voided.Error!;
        await roomCharges.DetachAllAsync(invoice.Id, cancellationToken);
        var cancelled = await CancelAdjustmentsAsync(invoice.Id, cancellationToken);
        if (cancelled.IsFailure)
            return cancelled.Error!;
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return invoice.ToDetail(now.ToBusinessDate());
    }

    /// <summary>BL-BR-15: hóa đơn nguồn bị hủy ⇒ khoản điều chỉnh chỉ số trỏ tới nó (chưa nằm trên hóa đơn đã chốt) tự hủy — lập lại đã đúng số.</summary>
    private async Task<Result> CancelAdjustmentsAsync(Guid sourceInvoiceId, CancellationToken ct)
    {
        foreach (var charge in await charges.ListReadingAdjustmentsAsync(sourceInvoiceId, ct))
        {
            if (charge.InvoiceId is { } holderId && (await invoices.GetForUpdateAsync(holderId, ct))?.Status != InvoiceStatus.Draft)
                continue;
            var cancelled = await adjustments.CancelAsync(charge, "Hóa đơn nguồn đã hủy — chỉ số đã đúng khi lập lại", ct);
            if (cancelled.IsFailure)
                return cancelled;
        }
        return Result.Success();
    }
}
