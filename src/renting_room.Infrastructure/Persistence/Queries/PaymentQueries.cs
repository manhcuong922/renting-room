using Microsoft.EntityFrameworkCore;
using renting_room.Application.Payments;
using renting_room.Domain.Payments;

namespace renting_room.Infrastructure.Persistence.Queries;

internal sealed class PaymentQueries(AppDbContext db) : IPaymentQueries
{
    public async Task<IReadOnlyList<Payment>> ListAsync(ListPaymentsQuery request, int maxRows, CancellationToken cancellationToken)
    {
        var query = db.Payments.AsNoTracking().Include(p => p.Allocations).AsQueryable();
        if (request.ContractId is { } contractId)
            query = query.Where(p => p.ContractId == contractId);
        if (request.RoomId is { } roomId)
            query = query.Where(p => db.Contracts.Any(c => c.Id == p.ContractId && c.RoomId == roomId));
        if (request.InvoiceId is { } invoiceId)
            query = query.Where(p => p.Allocations.Any(a => a.InvoiceId == invoiceId));

        return await query.OrderByDescending(p => p.PaidAt).ThenByDescending(p => p.ReceiptNo).Take(maxRows).ToListAsync(cancellationToken);
    }

    public Task<Guid?> GetContractIdAsync(Guid paymentId, CancellationToken cancellationToken) =>
        db.Payments.Where(p => p.Id == paymentId).Select(p => (Guid?)p.ContractId).FirstOrDefaultAsync(cancellationToken);
}
