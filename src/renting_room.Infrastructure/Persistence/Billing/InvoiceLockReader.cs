using Microsoft.EntityFrameworkCore;
using renting_room.Application.Common.Interfaces;
using renting_room.Domain.Billing;

namespace renting_room.Infrastructure.Persistence;

public sealed class InvoiceLockReader(AppDbContext db) : IInvoiceLockReader
{
    public async Task<DateOnly?> GetFirstOpenPeriodStartAsync(Guid contractId, CancellationToken cancellationToken)
    {
        var lastEnd = await db.Invoices.Where(i => i.ContractId == contractId && i.Status == InvoiceStatus.Finalized)
            .MaxAsync(i => (DateOnly?)i.PeriodEnd, cancellationToken);
        // M07 BL-BR-46: kỳ nằm trong khoảng ngắt tính tiền cũng đã khóa.
        var gapEnd = await db.ContractBillingGaps.Where(g => g.ContractId == contractId).MaxAsync(g => (DateOnly?)g.ToDate, cancellationToken);
        var locked = lastEnd is { } a && gapEnd is { } b ? (a > b ? a : b) : lastEnd ?? gapEnd;
        return locked?.AddDays(1);
    }
}
