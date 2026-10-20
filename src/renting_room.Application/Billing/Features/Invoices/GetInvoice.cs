using Mediator;
using renting_room.Application.Contracts;
using renting_room.Domain.Billing;
using renting_room.Domain.Common;

namespace renting_room.Application.Billing;

public sealed record GetInvoiceQuery(Guid Id) : IRequest<Result<InvoiceDetailDto>>;

public sealed class GetInvoiceHandler(IInvoiceQueries invoices, IContractQueries contracts, TimeProvider clock)
    : IRequestHandler<GetInvoiceQuery, Result<InvoiceDetailDto>>
{
    public async ValueTask<Result<InvoiceDetailDto>> Handle(GetInvoiceQuery request, CancellationToken cancellationToken)
    {
        var invoice = await invoices.GetDetailsAsync(request.Id, cancellationToken);
        if (invoice is null)
            return BillingErrors.NotFound;

        var previous = await invoices.ListPreviousDebtsAsync(invoice.ContractId, invoice.Id, invoice.PeriodStart, cancellationToken);
        var own = invoice.Status == InvoiceStatus.Draft ? Math.Max(invoice.TotalAmount, 0) : invoice.Outstanding;
        var earlier = await PreviousContractDebtsAsync(invoice.ContractId, cancellationToken);
        return invoice.ToDetail(clock.GetUtcNow().ToBusinessDate()) with
        {
            PreviousDebts = previous, TotalDue = own + previous.Sum(d => d.Outstanding),
            PreviousContractDebts = earlier, PreviousContractDue = earlier.Sum(d => d.Outstanding)
        };
    }

    private const int MaxContractChain = 10;

    private async Task<IReadOnlyList<PreviousContractDebtDto>> PreviousContractDebtsAsync(Guid contractId, CancellationToken ct)
    {
        var numbers = new Dictionary<Guid, string>();
        var cursor = await contracts.GetPreviousContractIdAsync(contractId, ct);
        while (cursor is { } id && numbers.Count < MaxContractChain && !numbers.ContainsKey(id))
        {
            var earlier = await contracts.GetChainLinkAsync(id, ct);
            if (earlier is null)
                break;
            numbers[id] = earlier.ContractNo;
            cursor = earlier.PreviousContractId;
        }
        if (numbers.Count == 0)
            return [];

        var ids = numbers.Keys.ToList();
        var debts = await invoices.ListOpenDebtsAsync(ids, ct);
        return debts.Select(d => new PreviousContractDebtDto(
            d.ContractId, numbers[d.ContractId], d.InvoiceId, d.InvoiceNo, $"{d.BillingMonth:yyyy-MM}", d.DueDate, d.Outstanding)).ToList();
    }
}
