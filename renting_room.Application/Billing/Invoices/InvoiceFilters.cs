using FluentValidation;
using Microsoft.EntityFrameworkCore;
using renting_room.Application.Common.Interfaces;
using renting_room.Domain.Billing;

namespace renting_room.Application.Billing;

// Truy vấn
internal static class InvoiceFilters
{
    /// <summary>Bộ lọc dùng chung cho danh sách phiếu và số tổng hợp (BL-UC-02).</summary>
    public static IQueryable<Invoice> Apply(IAppDbContext db, ListInvoicesQuery request)
    {
        var query = db.Invoices.AsNoTracking();
        if (request.PropertyId is { } propertyId)
            query = query.Where(i => i.PropertyId == propertyId);
        if (BillingMonths.Parse(request.BillingMonth) is { } month)
            query = query.Where(i => i.BillingMonth == month);
        if (request.Status is { } status)
            query = query.Where(i => i.Status == status);
        if (request.RoomId is { } roomId)
            query = query.Where(i => i.RoomId == roomId);
        if (request.ContractId is { } contractId)
            query = query.Where(i => i.ContractId == contractId);
        if (!string.IsNullOrWhiteSpace(request.Floor))
            query = query.Where(i => db.Rooms.Any(r => r.Id == i.RoomId && r.Floor == request.Floor.Trim()));
        if (request.UnpaidOnly == true)
            query = query.Where(i => i.Status == InvoiceStatus.Finalized && i.PaidAmount < i.TotalAmount);
        if (request.Stale == true)
            query = query.Where(i => i.Status == InvoiceStatus.Draft && i.IsStale);
        return query;
    }
}
