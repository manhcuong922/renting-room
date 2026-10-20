using Microsoft.EntityFrameworkCore;
using renting_room.Application.Billing;
using renting_room.Domain.Billing;

namespace renting_room.Infrastructure.Persistence.Repositories;

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

    public Task<RoomCharge?> FindReadingAdjustmentForUpdateAsync(
        Guid sourceInvoiceId, Guid feeTypeId, DateOnly usageStart, CancellationToken cancellationToken) =>
        db.RoomCharges.FirstOrDefaultAsync(c => c.SourceInvoiceId == sourceInvoiceId && c.FeeTypeId == feeTypeId
            && c.SourceUsageStart == usageStart && c.CancelledAt == null, cancellationToken);

    public async Task<IReadOnlyList<RoomCharge>> ListReadingAdjustmentsAsync(Guid sourceInvoiceId, CancellationToken cancellationToken) =>
        await db.RoomCharges.Where(c => c.SourceInvoiceId == sourceInvoiceId && c.CancelledAt == null).ToListAsync(cancellationToken);

    public void Add(RoomCharge charge) => db.RoomCharges.Add(charge);
}
