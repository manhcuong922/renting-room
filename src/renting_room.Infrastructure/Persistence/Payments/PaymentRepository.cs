using Microsoft.EntityFrameworkCore;
using renting_room.Application.Payments;
using renting_room.Domain.Payments;

namespace renting_room.Infrastructure.Persistence.Repositories;

internal sealed class PaymentRepository(AppDbContext db) : IPaymentRepository
{
    public async Task<IReadOnlyList<Payment>> ListByContractsAsync(IReadOnlyCollection<Guid> contractIds, CancellationToken cancellationToken) =>
        await db.Payments.Where(p => contractIds.Contains(p.ContractId)).ToListAsync(cancellationToken);

    public Task<Payment?> GetWithAllocationsForUpdateAsync(Guid id, CancellationToken cancellationToken) =>
        db.Payments.Include(p => p.Allocations).FirstOrDefaultAsync(p => p.Id == id, cancellationToken);

    public async Task<IReadOnlyList<Payment>> ListActiveWriteOffsForInvoiceAsync(Guid invoiceId, CancellationToken cancellationToken) =>
        await db.Payments.Include(p => p.Allocations)
            .Where(p => p.Kind == PaymentKind.WriteOff && p.Status == PaymentStatus.Recorded
                && p.Allocations.Any(a => a.InvoiceId == invoiceId && a.CancelledAt == null))
            .OrderByDescending(p => p.PaidAt).ThenByDescending(p => p.CreatedAt)
            .ToListAsync(cancellationToken);

    public void Add(Payment payment) => db.Payments.Add(payment);
}
