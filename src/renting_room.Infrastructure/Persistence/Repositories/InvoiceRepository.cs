using Microsoft.EntityFrameworkCore;
using renting_room.Application.Billing;
using renting_room.Domain.Billing;

namespace renting_room.Infrastructure.Persistence.Repositories;

internal sealed class InvoiceRepository(AppDbContext db) : IInvoiceRepository
{
    public Task<Invoice?> GetForUpdateAsync(Guid id, CancellationToken cancellationToken) =>
        db.Invoices.Include(i => i.Lines).Include(i => i.Segments).AsSplitQuery().FirstOrDefaultAsync(i => i.Id == id, cancellationToken);

    public async Task<IReadOnlyList<Invoice>> ListWithDetailsByContractAsync(Guid contractId, CancellationToken cancellationToken) =>
        await db.Invoices.Include(i => i.Lines).Include(i => i.Segments).AsSplitQuery()
            .Where(i => i.ContractId == contractId && i.Status != InvoiceStatus.Void).ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<Invoice>> ListByContractsAsync(IReadOnlyCollection<Guid> contractIds, CancellationToken cancellationToken) =>
        await db.Invoices.Where(i => contractIds.Contains(i.ContractId)).ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<Invoice>> ListByRepresentativeNameAsync(
        IReadOnlyCollection<Guid> contractIds, IReadOnlyCollection<Guid> excludedContractIds, string representativeName,
        CancellationToken cancellationToken) =>
        await db.Invoices
            .Where(i => contractIds.Contains(i.ContractId) && !excludedContractIds.Contains(i.ContractId) && i.SnapshotRepresentativeName == representativeName)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<Invoice>> LockOpenForUpdateAsync(Guid contractId, CancellationToken cancellationToken)
    {
        var openIds = await db.Invoices.Where(i => i.ContractId == contractId && i.Status == InvoiceStatus.Finalized && i.PaidAmount < i.TotalAmount)
            .Select(i => i.Id).OrderBy(id => id).ToListAsync(cancellationToken);
        foreach (var id in openIds)
            await db.LockForUpdateAsync<Invoice>(id, cancellationToken);
        return await db.Invoices.Where(i => openIds.Contains(i.Id)).ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyDictionary<Guid, Invoice>> LockForUpdateAsync(IReadOnlyCollection<Guid> invoiceIds, CancellationToken cancellationToken)
    {
        foreach (var id in invoiceIds.Order())
            await db.LockForUpdateAsync<Invoice>(id, cancellationToken);
        return await db.Invoices.Where(i => invoiceIds.Contains(i.Id)).ToDictionaryAsync(i => i.Id, cancellationToken);
    }

    public Task<Invoice?> FindFinalForUpdateAsync(Guid contractId, CancellationToken cancellationToken) =>
        db.Invoices.FirstOrDefaultAsync(i => i.ContractId == contractId && i.Type == InvoiceType.Final && i.Status != InvoiceStatus.Void, cancellationToken);

    public void Add(Invoice invoice) => db.Invoices.Add(invoice);

    public void Remove(Invoice invoice) => db.Invoices.Remove(invoice);
}

internal sealed class RoomChargeRepository(AppDbContext db) : IRoomChargeRepository
{
    public Task<RoomCharge?> GetForUpdateAsync(Guid id, CancellationToken cancellationToken) =>
        db.RoomCharges.FirstOrDefaultAsync(c => c.Id == id, cancellationToken);

    public async Task<IReadOnlyList<RoomCharge>> ListPendingByContractAsync(Guid contractId, CancellationToken cancellationToken) =>
        await db.RoomCharges
            .Where(c => c.ContractId == contractId && c.InvoiceId == null && c.CancelledAt == null)
            .OrderBy(c => c.IncurredOn).ThenBy(c => c.CreatedAt).ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<RoomCharge>> ListByInvoiceAsync(Guid invoiceId, CancellationToken cancellationToken) =>
        await db.RoomCharges.Where(c => c.InvoiceId == invoiceId).ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<RoomCharge>> ListTransferChargesAsync(
        Guid contractId, Guid oldRoomId, DateOnly date, Guid? rentChargeId, CancellationToken cancellationToken) =>
        await db.RoomCharges
            .Where(c => c.ContractId == contractId && c.CancelledAt == null
                && ((c.MeterId != null && c.RoomId == oldRoomId && c.IncurredOn == date) || c.Id == rentChargeId))
            .ToListAsync(cancellationToken);

    public void Add(RoomCharge charge) => db.RoomCharges.Add(charge);
}
