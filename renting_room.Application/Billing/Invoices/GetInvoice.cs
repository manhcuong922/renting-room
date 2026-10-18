using FluentValidation;
using Mediator;
using Microsoft.EntityFrameworkCore;
using renting_room.Application.Common.Interfaces;
using renting_room.Domain.Billing;
using renting_room.Domain.Common;

namespace renting_room.Application.Billing;

public sealed record GetInvoiceQuery(Guid Id) : IRequest<Result<InvoiceDetailDto>>;

public sealed class GetInvoiceHandler(IAppDbContext db, TimeProvider clock) : IRequestHandler<GetInvoiceQuery, Result<InvoiceDetailDto>>
{
    public async ValueTask<Result<InvoiceDetailDto>> Handle(GetInvoiceQuery request, CancellationToken cancellationToken)
    {
        var invoice = await db.Invoices.AsNoTracking().Include(i => i.Lines).Include(i => i.Segments).AsSplitQuery()
            .FirstOrDefaultAsync(i => i.Id == request.Id, cancellationToken);
        if (invoice is null)
            return BillingErrors.NotFound;

        // F1 (PM-UC-18): phiếu kỳ trước của HĐ còn nợ — thu vẫn FIFO phiếu cũ nhất trước (PM-BR-06).
        var debts = await db.Invoices.AsNoTracking()
            .Where(i => i.ContractId == invoice.ContractId && i.Id != invoice.Id && i.Status == InvoiceStatus.Finalized
                && i.PaidAmount < i.TotalAmount && i.PeriodStart < invoice.PeriodStart)
            .OrderBy(i => i.DueDate).ThenBy(i => i.PeriodStart)
            .Select(i => new { i.Id, i.InvoiceNo, i.BillingMonth, i.DueDate, Outstanding = i.TotalAmount - i.PaidAmount })
            .ToListAsync(cancellationToken);
        var previous = debts.Select(d => new PreviousDebtDto(d.Id, d.InvoiceNo, $"{d.BillingMonth:yyyy-MM}", d.DueDate, d.Outstanding)).ToList();
        var own = invoice.Status == InvoiceStatus.Draft ? Math.Max(invoice.TotalAmount, 0) : invoice.Outstanding;
        var earlier = await PreviousContractDebtsAsync(invoice.ContractId, cancellationToken);
        return invoice.ToDetail(clock.GetUtcNow().ToBusinessDate()) with
        {
            PreviousDebts = previous, TotalDue = own + previous.Sum(d => d.Outstanding),
            PreviousContractDebts = earlier, PreviousContractDue = earlier.Sum(d => d.Outstanding)
        };
    }

    private const int MaxContractChain = 10;

    /// <summary>PM-BR-31: đi ngược chuỗi <c>previous_contract_id</c> (ký lại — CT-UC-21), lấy phiếu đã chốt còn nợ của các HĐ đó.</summary>
    private async Task<IReadOnlyList<PreviousContractDebtDto>> PreviousContractDebtsAsync(Guid contractId, CancellationToken ct)
    {
        var numbers = new Dictionary<Guid, string>();
        var cursor = await db.Contracts.Where(c => c.Id == contractId).Select(c => c.PreviousContractId).FirstOrDefaultAsync(ct);
        while (cursor is { } id && numbers.Count < MaxContractChain && !numbers.ContainsKey(id))
        {
            var earlier = await db.Contracts.Where(c => c.Id == id).Select(c => new { c.ContractNo, c.PreviousContractId }).FirstOrDefaultAsync(ct);
            if (earlier is null)
                break;
            numbers[id] = earlier.ContractNo;
            cursor = earlier.PreviousContractId;
        }
        if (numbers.Count == 0)
            return [];

        var ids = numbers.Keys.ToList();
        var debts = await db.Invoices.AsNoTracking()
            .Where(i => ids.Contains(i.ContractId) && i.Status == InvoiceStatus.Finalized && i.PaidAmount < i.TotalAmount)
            .OrderBy(i => i.DueDate).ThenBy(i => i.PeriodStart)
            .Select(i => new { i.ContractId, i.Id, i.InvoiceNo, i.BillingMonth, i.DueDate, Outstanding = i.TotalAmount - i.PaidAmount })
            .ToListAsync(ct);
        return debts.Select(d => new PreviousContractDebtDto(
            d.ContractId, numbers[d.ContractId], d.Id, d.InvoiceNo, $"{d.BillingMonth:yyyy-MM}", d.DueDate, d.Outstanding)).ToList();
    }
}
