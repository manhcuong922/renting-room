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
        return lastEnd?.AddDays(1);
    }
}
