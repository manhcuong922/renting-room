using Mediator;
using renting_room.Application.Contracts;
using renting_room.Application.Properties;
using renting_room.Domain.Billing;
using renting_room.Domain.Common;
using renting_room.Domain.Contracts;

namespace renting_room.Application.Billing;

public sealed record GetInvoiceQuery(Guid Id) : IRequest<Result<InvoiceDetailDto>>;

public sealed class GetInvoiceHandler(IInvoiceQueries invoices, IContractQueries contracts, IPropertyQueries properties, TimeProvider clock)
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
            PreviousContractDebts = earlier, PreviousContractDue = earlier.Sum(d => d.Outstanding),
            FinalPeriod = invoice.Type == InvoiceType.Final ? await FinalPeriodAsync(invoice, cancellationToken) : null
        };
    }

    private async Task<FinalPeriodSummaryDto?> FinalPeriodAsync(Invoice invoice, CancellationToken ct)
    {
        if (await contracts.GetAsync(invoice.ContractId, ct) is not { } contract)
            return null;
        var standard = (await properties.GetBillingAsync(contract.PropertyId, ct)).Schedule.StandardPeriodContaining(invoice.PeriodStart);
        var rentBilled = (await invoices.ListForBalanceAsync(contract.Id, ct))
            .FirstOrDefault(i => i.Type == InvoiceType.Regular && i.PeriodStart == invoice.PeriodStart)?.InvoiceNo;
        return new FinalPeriodSummaryDto(invoice.PeriodEnd, invoice.PeriodStart, standard.End, invoice.PeriodEnd.DayNumber - invoice.PeriodStart.DayNumber + 1,
            standard.Days, ChargeOf(invoice, contract.FinalPeriodCharge ?? FinalPeriodCharge.FullPeriod), rentBilled);
    }

    /// <summary>Nháp ⇒ lựa chọn đang áp của HĐ; đã chốt / đã hủy ⇒ suy từ dòng tiền phòng của chính hóa đơn (hóa đơn cũ hiện đúng cách đã tính).</summary>
    private static FinalPeriodCharge ChargeOf(Invoice invoice, FinalPeriodCharge current)
    {
        if (invoice.Status == InvoiceStatus.Draft
            || invoice.Lines.FirstOrDefault(l => l is { Type: InvoiceLineType.Rent, IsSystem: true }) is not { } rent)
            return current;
        return rent.ProrationFactor switch
        {
            0 => FinalPeriodCharge.None,
            { } factor when factor < 1 => FinalPeriodCharge.Daily,
            _ => FinalPeriodCharge.FullPeriod
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
