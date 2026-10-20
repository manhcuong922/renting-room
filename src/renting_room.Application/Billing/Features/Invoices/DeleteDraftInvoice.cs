using Mediator;
using renting_room.Application.Common.Interfaces;
using renting_room.Domain.Billing;
using renting_room.Domain.Common;

namespace renting_room.Application.Billing;

public sealed record DeleteDraftInvoiceCommand(Guid Id) : IRequest<Result>;

public sealed class DeleteDraftInvoiceHandler(
    IUnitOfWork unitOfWork, IInvoiceRepository invoices, IInvoiceQueries invoiceQueries, RoomChargeBilling roomCharges)
    : IRequestHandler<DeleteDraftInvoiceCommand, Result>
{
    public async ValueTask<Result> Handle(DeleteDraftInvoiceCommand request, CancellationToken cancellationToken)
    {
        await using var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken);
        await unitOfWork.LockForUpdateAsync<Invoice>(request.Id, cancellationToken);
        var invoice = await invoices.GetForUpdateAsync(request.Id, cancellationToken);
        if (invoice is null)
            return Result.Failure(BillingErrors.NotFound);
        if (invoice.Status != InvoiceStatus.Draft)
            return Result.Failure(BillingErrors.NotDraft);
        if (await invoiceQueries.HasLaterInvoiceAsync(invoice, cancellationToken))
            return Result.Failure(BillingErrors.NotLatest);

        await roomCharges.DetachAllAsync(invoice.Id, cancellationToken);
        invoices.Remove(invoice);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return Result.Success();
    }
}
