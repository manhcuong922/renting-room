using Microsoft.EntityFrameworkCore;
using renting_room.Application.Billing;
using renting_room.Domain.Billing;

namespace renting_room.Infrastructure.Persistence.Queries;

internal sealed class RoomChargeQueries(AppDbContext db) : IRoomChargeQueries
{
    public Task<int> CountPendingAsync(Guid contractId, CancellationToken cancellationToken) =>
        db.RoomCharges.CountAsync(c => c.ContractId == contractId && c.InvoiceId == null && c.CancelledAt == null, cancellationToken);

    public async Task<IReadOnlyList<RoomCharge>> ListAsync(ListRoomChargesQuery request, int maxRows, CancellationToken cancellationToken)
    {
        var query = db.RoomCharges.AsNoTracking().AsQueryable();
        if (request.RoomId is { } roomId)
            query = query.Where(c => c.RoomId == roomId);
        if (request.PropertyId is { } propertyId)
            query = query.Where(c => c.PropertyId == propertyId);
        if (request.From is { } from)
            query = query.Where(c => c.IncurredOn >= from);
        if (request.To is { } to)
            query = query.Where(c => c.IncurredOn <= to);
        query = request.Status switch
        {
            RoomChargeStatus.Cancelled => query.Where(c => c.CancelledAt != null),
            RoomChargeStatus.Pending => query.Where(c => c.CancelledAt == null && c.InvoiceId == null),
            RoomChargeStatus.OnDraft => query.Where(c => db.Invoices.Any(i => i.Id == c.InvoiceId && i.Status == InvoiceStatus.Draft)),
            RoomChargeStatus.Billed => query.Where(c => db.Invoices.Any(i => i.Id == c.InvoiceId && i.Status != InvoiceStatus.Draft)),
            _ => query
        };

        return await query.OrderByDescending(c => c.IncurredOn).ThenByDescending(c => c.CreatedAt).Take(maxRows).ToListAsync(cancellationToken);
    }

    public Task<Guid?> GetContractIdAsync(Guid chargeId, CancellationToken cancellationToken) =>
        db.RoomCharges.Where(c => c.Id == chargeId).Select(c => (Guid?)c.ContractId).FirstOrDefaultAsync(cancellationToken);

    public Task<bool> IsSystemAsync(Guid chargeId, CancellationToken cancellationToken) =>
        db.RoomCharges.AnyAsync(c => c.Id == chargeId && (c.MeterId != null || c.SourceInvoiceId != null), cancellationToken);

    public async Task<decimal> GetWaitingCreditTotalAsync(Guid contractId, CancellationToken cancellationToken) =>
        await db.RoomCharges
            .Where(c => c.ContractId == contractId && c.Kind == RoomChargeKind.Credit && c.CancelledAt == null && !c.IsSettled && c.SourceInvoiceId == null
                && (c.InvoiceId == null || db.Invoices.Any(i => i.Id == c.InvoiceId && i.Status == InvoiceStatus.Draft)))
            .SumAsync(c => (decimal?)c.Amount, cancellationToken) ?? 0;

    public Task<SettledCredit?> FindSettledCreditAsync(Guid chargeId, CancellationToken cancellationToken) =>
        db.RoomCharges.AsNoTracking()
            .Where(c => c.Id == chargeId && c.Kind == RoomChargeKind.Credit && c.IsSettled && c.CancelledAt == null && c.SourceInvoiceId == null)
            .Select(c => new SettledCredit(c.ContractId, c.IncurredOn, c.Amount)).FirstOrDefaultAsync(cancellationToken);
}
