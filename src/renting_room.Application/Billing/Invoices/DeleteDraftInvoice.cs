using Mediator;
using renting_room.Application.Common.Interfaces;
using renting_room.Domain.Billing;
using renting_room.Domain.Common;

namespace renting_room.Application.Billing;

public sealed record DeleteDraftInvoiceCommand(Guid Id) : IRequest<Result>;

public sealed class DeleteDraftInvoiceHandler(IAppDbContext db) : IRequestHandler<DeleteDraftInvoiceCommand, Result>
{
    public async ValueTask<Result> Handle(DeleteDraftInvoiceCommand request, CancellationToken cancellationToken)
    {
        await using var transaction = await db.BeginTransactionAsync(cancellationToken);
        await db.LockForUpdateAsync<Invoice>(request.Id, cancellationToken);
        var invoice = await InvoiceAccess.LoadAsync(db, request.Id, cancellationToken);
        if (invoice is null)
            return Result.Failure(BillingErrors.NotFound);
        if (invoice.Status != InvoiceStatus.Draft)
            return Result.Failure(BillingErrors.NotDraft);
        if (await InvoiceAccess.HasLaterInvoiceAsync(db, invoice, cancellationToken))
            return Result.Failure(BillingErrors.NotLatest);

        // BL-BR-35: khoản phát sinh của nháp quay về chờ.
        await RoomChargeBilling.DetachAllAsync(db, invoice.Id, cancellationToken);
        db.Invoices.Remove(invoice);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return Result.Success();
    }
}
