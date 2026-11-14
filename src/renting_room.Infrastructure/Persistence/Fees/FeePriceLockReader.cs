using Microsoft.EntityFrameworkCore;
using renting_room.Application.Common.Interfaces;
using renting_room.Domain.Billing;

namespace renting_room.Infrastructure.Persistence;

public sealed class FeePriceLockReader(AppDbContext db) : IFeePriceLockReader
{
    public async Task<IReadOnlyCollection<DateOnly>> GetLockedDatesAsync(Guid feeTypeId, CancellationToken cancellationToken) =>
        await db.Invoices.Where(i => i.Status == InvoiceStatus.Finalized)
            .SelectMany(i => i.Lines)
            .Where(l => l.FeeTypeId == feeTypeId)
            .Select(l => l.ServiceTo)
            .Distinct()
            .ToListAsync(cancellationToken);
}
