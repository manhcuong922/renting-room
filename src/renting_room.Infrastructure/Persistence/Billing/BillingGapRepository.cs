using Microsoft.EntityFrameworkCore;
using renting_room.Application.Billing;
using renting_room.Domain.Billing;

namespace renting_room.Infrastructure.Persistence.Repositories;

internal sealed class BillingGapRepository(AppDbContext db) : IBillingGapRepository
{
    public async Task<IReadOnlyList<ContractBillingGap>> ListByContractAsync(Guid contractId, CancellationToken cancellationToken) =>
        await db.ContractBillingGaps.Where(g => g.ContractId == contractId).OrderBy(g => g.FromDate).ToListAsync(cancellationToken);

    public Task<ContractBillingGap?> FindResumedByAsync(Guid invoiceId, CancellationToken cancellationToken) =>
        db.ContractBillingGaps.FirstOrDefaultAsync(g => g.ResumedInvoiceId == invoiceId, cancellationToken);

    public void Add(ContractBillingGap gap) => db.ContractBillingGaps.Add(gap);

    public void Remove(ContractBillingGap gap) => db.ContractBillingGaps.Remove(gap);
}
