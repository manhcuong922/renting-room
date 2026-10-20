using Mediator;
using renting_room.Domain.Common;

namespace renting_room.Application.Billing;

public sealed record InvoiceTotalsDto(
    int FinalizedCount, decimal TotalAmount, decimal PaidAmount, decimal OutstandingAmount, decimal OverdueAmount, int DraftCount, decimal DraftAmount,
    decimal CollectedAmount = 0, decimal WrittenOffAmount = 0);

public sealed record GetInvoiceTotalsQuery(ListInvoicesQuery Filter) : IRequest<InvoiceTotalsDto>;

public sealed class GetInvoiceTotalsHandler(IInvoiceQueries invoices, TimeProvider clock) : IRequestHandler<GetInvoiceTotalsQuery, InvoiceTotalsDto>
{
    public async ValueTask<InvoiceTotalsDto> Handle(GetInvoiceTotalsQuery request, CancellationToken cancellationToken) =>
        await invoices.GetTotalsAsync(request.Filter, clock.GetUtcNow().ToBusinessDate(), cancellationToken);
}
