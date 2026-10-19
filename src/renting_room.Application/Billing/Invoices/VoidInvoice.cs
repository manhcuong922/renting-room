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

/// <summary>BL-BR-15/22: hủy phiếu đã chốt chưa thu tiền, chỉ phiếu mới nhất của HĐ — chỉ số được giải phóng để lập lại.</summary>
public sealed class VoidInvoiceHandler(
    IUnitOfWork unitOfWork, IInvoiceRepository invoices, IInvoiceQueries invoiceQueries, RoomChargeBilling roomCharges, TimeProvider clock)
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
        // BL-BR-35: khoản phát sinh của phiếu bị hủy quay về chờ, vào phiếu lập lại.
        await roomCharges.DetachAllAsync(invoice.Id, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return invoice.ToDetail(now.ToBusinessDate());
    }
}
