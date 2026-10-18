using Microsoft.EntityFrameworkCore;
using renting_room.Application.Common.Interfaces;
using renting_room.Domain.Billing;
using renting_room.Domain.Properties;

namespace renting_room.Application.Properties;

/// <summary>Cài đặt kỳ thu của khu (PR-BR-09) dùng khi tính kỳ / lập phiếu — mọi HĐ của khu dùng chung.</summary>
public sealed record PropertyBilling(BillingSchedule Schedule, ProrationMode ProrationMode, int PaymentDueDays, bool RoundInvoiceTotal = true)
{
    public static async Task<PropertyBilling> LoadAsync(IAppDbContext db, Guid propertyId, CancellationToken ct)
    {
        var property = await db.Properties.AsNoTracking().Where(p => p.Id == propertyId)
            .Select(p => new { p.BillingScheduleEntries, p.ProrationMode, p.PaymentDueDays, p.RoundInvoiceTotal })
            .FirstAsync(ct);
        return new PropertyBilling(new BillingSchedule(property.BillingScheduleEntries), property.ProrationMode, property.PaymentDueDays,
            property.RoundInvoiceTotal);
    }

    public static async Task<IReadOnlyDictionary<Guid, PropertyBilling>> LoadManyAsync(
        IAppDbContext db, IEnumerable<Guid> propertyIds, CancellationToken ct)
    {
        var ids = propertyIds.Distinct().ToList();
        var properties = await db.Properties.AsNoTracking().Where(p => ids.Contains(p.Id))
            .Select(p => new { p.Id, p.BillingScheduleEntries, p.ProrationMode, p.PaymentDueDays, p.RoundInvoiceTotal })
            .ToListAsync(ct);
        return properties.ToDictionary(
            p => p.Id,
            p => new PropertyBilling(new BillingSchedule(p.BillingScheduleEntries), p.ProrationMode, p.PaymentDueDays, p.RoundInvoiceTotal));
    }
}
