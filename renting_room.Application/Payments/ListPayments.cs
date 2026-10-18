using FluentValidation;
using Mediator;
using Microsoft.EntityFrameworkCore;
using renting_room.Application.Common.Interfaces;

namespace renting_room.Application.Payments;

// Danh sách
public sealed record ListPaymentsQuery(Guid? ContractId, Guid? RoomId, Guid? InvoiceId) : IRequest<IReadOnlyList<PaymentDto>>;

public sealed class ListPaymentsHandler(IAppDbContext db) : IRequestHandler<ListPaymentsQuery, IReadOnlyList<PaymentDto>>
{
    private const int MaxRows = 500;

    public async ValueTask<IReadOnlyList<PaymentDto>> Handle(ListPaymentsQuery request, CancellationToken cancellationToken)
    {
        var query = db.Payments.AsNoTracking().Include(p => p.Allocations).AsQueryable();
        if (request.ContractId is { } contractId)
            query = query.Where(p => p.ContractId == contractId);
        if (request.RoomId is { } roomId)
            query = query.Where(p => db.Contracts.Any(c => c.Id == p.ContractId && c.RoomId == roomId));
        if (request.InvoiceId is { } invoiceId)
            query = query.Where(p => p.Allocations.Any(a => a.InvoiceId == invoiceId));

        var payments = await query.OrderByDescending(p => p.PaidAt).ThenByDescending(p => p.ReceiptNo).Take(MaxRows).ToListAsync(cancellationToken);
        return await PaymentMapping.ToDtosAsync(db, payments, cancellationToken);
    }
}
