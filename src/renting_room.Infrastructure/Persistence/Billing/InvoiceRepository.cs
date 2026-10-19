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

    /// <summary>Bảng invoices / meters / fee_types không qua global filter trong SQL thô ⇒ luôn kèm điều kiện tổ chức.</summary>
    public Task MarkDraftsStaleAsync(DraftStaleScope scope, CancellationToken cancellationToken)
    {
        var (organizationId, priceFrom) = (scope.OrganizationId, scope.PriceFrom);
        var contracts = scope.ContractIds.ToArray();
        var rooms = scope.RoomIds.ToArray();
        var meters = scope.MeterIds.ToArray();
        var feeTypes = scope.FeeTypeIds.ToArray();
        return db.Database.ExecuteSqlAsync($"""
            UPDATE invoices SET is_stale = TRUE
            WHERE organization_id = {organizationId} AND status = 'Draft' AND NOT is_stale
              AND (contract_id = ANY({contracts})
                OR room_id = ANY({rooms})
                OR room_id IN (SELECT m.room_id FROM meters m WHERE m.organization_id = {organizationId} AND m.id = ANY({meters}))
                OR (period_end >= {priceFrom}
                    AND property_id IN (SELECT f.property_id FROM fee_types f WHERE f.organization_id = {organizationId} AND f.id = ANY({feeTypes}))))
            """, cancellationToken);
    }
}
