using Mediator;
using renting_room.Application.Common.Interfaces;
using renting_room.Application.Contracts;
using renting_room.Domain.Common;

namespace renting_room.Application.Billing;

public sealed record CancelInvoiceRefundCommand(Guid Id) : IRequest<Result<InvoiceDetailDto>>;

public sealed class CancelInvoiceRefundHandler(
    IUnitOfWork unitOfWork, IInvoiceRepository invoices, IInvoiceQueries invoiceQueries, IContractQueries contracts, TimeProvider clock)
    : IRequestHandler<CancelInvoiceRefundCommand, Result<InvoiceDetailDto>>
{
    public async ValueTask<Result<InvoiceDetailDto>> Handle(CancelInvoiceRefundCommand request, CancellationToken cancellationToken) =>
        await RefundMutation.RunAsync(unitOfWork, invoices, invoiceQueries, contracts, request.Id, clock.GetUtcNow().ToBusinessDate(),
            invoice => invoice.CancelRefund(), cancellationToken);
}
