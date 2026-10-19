using Microsoft.EntityFrameworkCore;
using renting_room.Application.Common.Models;
using renting_room.Application.Identity.Organizations;
using renting_room.Application.Properties;
using renting_room.Domain.Billing;
using renting_room.Domain.Contracts;
using renting_room.Domain.Properties;

namespace renting_room.Infrastructure.Persistence.Queries;

internal sealed class PropertyQueries(AppDbContext db, IOrganizationQueries organizations) : IPropertyQueries
{
    public Task<bool> ExistsAsync(Guid id, CancellationToken cancellationToken) =>
        db.Properties.AnyAsync(p => p.Id == id, cancellationToken);

    public Task<int> CountAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken) =>
        db.Properties.CountAsync(p => ids.Contains(p.Id), cancellationToken);

    public Task<string?> GetCodeAsync(Guid id, CancellationToken cancellationToken) =>
        db.Properties.Where(p => p.Id == id).Select(p => p.Code).FirstOrDefaultAsync(cancellationToken);

    public async Task<PropertyStatus?> GetStatusAsync(Guid id, CancellationToken cancellationToken) =>
        await db.Properties.Where(p => p.Id == id)
            .Select(p => new PropertyStatus(p.Id, p.ArchivedAt != null))
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<IReadOnlyList<PropertyStatus>> GetStatusesAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken) =>
        await db.Properties.Where(p => ids.Contains(p.Id))
            .Select(p => new PropertyStatus(p.Id, p.ArchivedAt != null))
            .ToListAsync(cancellationToken);

    public Task<Property?> GetAsync(Guid id, CancellationToken cancellationToken) =>
        db.Properties.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id, cancellationToken);

    public async Task<PagedResult<PropertySummaryDto>> ListAsync(ListPropertiesQuery request, DateOnly today, CancellationToken cancellationToken)
    {
        var query = db.Properties.AsNoTracking();
        if (!request.IncludeArchived)
            query = query.Where(p => p.ArchivedAt == null);
        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var term = request.Search.Trim().ToUpper();
            query = query.Where(p => p.Code.Contains(term) || p.Name.ToUpper().Contains(term));
        }

        var total = await query.CountAsync(cancellationToken);
        // PR-BR-02: số phòng đang thuê đếm cùng điều kiện với trạng thái phòng (ngày chuyển phòng, ngày trả phòng vẫn tính).
        var occupied = db.OccupiedRoomIds(today);
        var rows = await query
            .OrderBy(p => p.Code)
            .Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize)
            .Select(p => new
            {
                Property = p,
                RoomCount = db.Rooms.Count(r => r.PropertyId == p.Id && r.ArchivedAt == null),
                OccupiedRoomCount = db.Rooms.Count(r => r.PropertyId == p.Id && occupied.Contains(r.Id))
            })
            .ToListAsync(cancellationToken);

        // PR-BR-17: khu không khai bên cho thuê riêng ⇒ xét thông tin chủ trọ.
        var organizationLessorComplete = rows.Count > 0
            && (await organizations.GetDefaultLessorAsync(rows[0].Property.OrganizationId, cancellationToken))?.IsComplete(today) == true;
        var items = rows.Select(r => new PropertySummaryDto(
                r.Property.Id, r.Property.Code, r.Property.Name, r.Property.Address.FullText, r.RoomCount, r.OccupiedRoomCount,
                r.Property.Lessor?.IsComplete(today) ?? organizationLessorComplete, r.Property.IsArchived))
            .ToList();

        return new PagedResult<PropertySummaryDto>(items, request.Page, request.PageSize, total);
    }

    public Task<bool> HasOpenContractsAsync(Guid propertyId, DateOnly today, CancellationToken cancellationToken) =>
        db.Contracts.AnyAsync(c => c.PropertyId == propertyId
            && (c.Status == ContractStatus.Draft || c.Status == ContractStatus.Active || c.Status == ContractStatus.Liquidating
                || (c.Status == ContractStatus.Ended && c.ActualEndDate >= today)),
            cancellationToken);

    public Task<DateOnly?> GetLastInvoicePeriodEndAsync(Guid propertyId, CancellationToken cancellationToken) =>
        db.Invoices.Where(i => i.PropertyId == propertyId && i.Status != InvoiceStatus.Void)
            .MaxAsync(i => (DateOnly?)i.PeriodEnd, cancellationToken);

    public Task<bool> HasDraftInvoicesAsync(Guid propertyId, CancellationToken cancellationToken) =>
        db.Invoices.AnyAsync(i => i.PropertyId == propertyId && i.Status == InvoiceStatus.Draft, cancellationToken);

    public async Task<PropertyBilling> GetBillingAsync(Guid propertyId, CancellationToken cancellationToken)
    {
        var property = await db.Properties.AsNoTracking().Where(p => p.Id == propertyId)
            .Select(p => new { p.BillingScheduleEntries, p.ProrationMode, p.PaymentDueDays, p.RoundInvoiceTotal })
            .FirstAsync(cancellationToken);
        return new PropertyBilling(new BillingSchedule(property.BillingScheduleEntries), property.ProrationMode, property.PaymentDueDays,
            property.RoundInvoiceTotal);
    }

    public async Task<IReadOnlyList<PropertyCodeRow>> ListCodesAsync(IReadOnlyCollection<Guid>? ids, CancellationToken cancellationToken)
    {
        var properties = db.Properties.AsNoTracking();
        properties = ids is { Count: > 0 } ? properties.Where(p => ids.Contains(p.Id)) : properties.Where(p => p.ArchivedAt == null);
        return await properties.OrderBy(p => p.Code).Select(p => new PropertyCodeRow(p.Id, p.Code)).ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyDictionary<Guid, PropertyBilling>> GetBillingsAsync(IEnumerable<Guid> propertyIds, CancellationToken cancellationToken)
    {
        var ids = propertyIds.Distinct().ToList();
        var properties = await db.Properties.AsNoTracking().Where(p => ids.Contains(p.Id))
            .Select(p => new { p.Id, p.BillingScheduleEntries, p.ProrationMode, p.PaymentDueDays, p.RoundInvoiceTotal })
            .ToListAsync(cancellationToken);
        return properties.ToDictionary(
            p => p.Id,
            p => new PropertyBilling(new BillingSchedule(p.BillingScheduleEntries), p.ProrationMode, p.PaymentDueDays, p.RoundInvoiceTotal));
    }
}
