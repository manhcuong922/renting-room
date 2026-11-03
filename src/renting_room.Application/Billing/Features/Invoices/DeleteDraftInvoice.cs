using Mediator;
using renting_room.Application.Common.Interfaces;
using renting_room.Domain.Billing;
using renting_room.Domain.Common;
using renting_room.Domain.Contracts;

namespace renting_room.Application.Billing;

public sealed record DeleteDraftInvoiceCommand(Guid Id) : IRequest<Result>;

public sealed class DeleteDraftInvoiceHandler(
    IUnitOfWork unitOfWork, IInvoiceRepository invoices, IInvoiceQueries invoiceQueries, RoomChargeBilling roomCharges, IBillingGapRepository gaps)
    : IRequestHandler<DeleteDraftInvoiceCommand, Result>
{
    public async ValueTask<Result> Handle(DeleteDraftInvoiceCommand request, CancellationToken cancellationToken)
    {
        var contractId = await invoiceQueries.GetContractIdAsync(request.Id, cancellationToken);
        if (contractId is null)
            return Result.Failure(BillingErrors.NotFound);

        await using var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken);
        // BL-BR-22 (M-21): khóa HĐ trước phiếu như tạo phiếu — không thì tạo kỳ sau song song lọt kiểm tra "mới nhất" ⇒ mất kỳ này, không khoảng ngắt.
        await unitOfWork.LockForUpdateAsync<Contract>(contractId.Value, cancellationToken);
        await unitOfWork.LockForUpdateAsync<Invoice>(request.Id, cancellationToken);
        var invoice = await invoices.GetForUpdateAsync(request.Id, cancellationToken);
        if (invoice is null)
            return Result.Failure(BillingErrors.NotFound);
        if (invoice.Status != InvoiceStatus.Draft)
            return Result.Failure(BillingErrors.NotDraft);
        if (await invoiceQueries.HasLaterInvoiceAsync(invoice, cancellationToken))
            return Result.Failure(BillingErrors.NotLatest);

        await roomCharges.DetachAllAsync(invoice.Id, cancellationToken);
        // BL-BR-45: khoảng ngắt gắn với phiếu đầu tiên sau nó ⇒ xóa nháp đó thì khoảng mất theo.
        if (await gaps.FindResumedByAsync(invoice.Id, cancellationToken) is { } gap)
            gaps.Remove(gap);
        invoices.Remove(invoice);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return Result.Success();
    }
}
