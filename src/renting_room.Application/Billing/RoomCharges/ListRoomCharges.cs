using FluentValidation;
using Mediator;
using Microsoft.EntityFrameworkCore;
using renting_room.Application.Common.Interfaces;
using renting_room.Domain.Billing;

namespace renting_room.Application.Billing;

public sealed record ListRoomChargesQuery(Guid? RoomId, Guid? PropertyId, RoomChargeStatus? Status, DateOnly? From, DateOnly? To)
    : IRequest<IReadOnlyList<RoomChargeDto>>;

/// <summary>BL-UC-17: khoản phát sinh theo phòng / khu, mới nhất trước — chủ trọ ở xa xem người quản lý đã ghi gì.</summary>
public sealed class ListRoomChargesHandler(IAppDbContext db) : IRequestHandler<ListRoomChargesQuery, IReadOnlyList<RoomChargeDto>>
{
    private const int MaxRows = 500;

    public async ValueTask<IReadOnlyList<RoomChargeDto>> Handle(ListRoomChargesQuery request, CancellationToken cancellationToken)
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

        var charges = await query.OrderByDescending(c => c.IncurredOn).ThenByDescending(c => c.CreatedAt).Take(MaxRows).ToListAsync(cancellationToken);
        return await RoomChargeBilling.ToDtosAsync(db, charges, cancellationToken);
    }
}
