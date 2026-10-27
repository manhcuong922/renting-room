using renting_room.Domain.Billing;

namespace renting_room.Application.Billing;

/// <summary>BL-BR-41: hóa đơn đã chuyển nợ hiện trạng thái theo hóa đơn trả phòng nhận nợ — tra một lượt cho cả trang.</summary>
internal static class CarriedTargets
{
    public static async Task<IReadOnlyDictionary<Guid, CarriedTarget>> LoadAsync(
        this IInvoiceQueries invoices, IEnumerable<Invoice> items, DateOnly today, CancellationToken ct)
    {
        var ids = items.Select(i => i.CarriedToInvoiceId).OfType<Guid>().Distinct().ToList();
        return ids.Count == 0 ? new Dictionary<Guid, CarriedTarget>() : await invoices.GetCarriedTargetsAsync(ids, today, ct);
    }

    public static async Task<CarriedTarget?> LoadAsync(this IInvoiceQueries invoices, Invoice invoice, DateOnly today, CancellationToken ct) =>
        (await invoices.LoadAsync([invoice], today, ct)).For(invoice);

    public static CarriedTarget? For(this IReadOnlyDictionary<Guid, CarriedTarget> targets, Invoice invoice) =>
        invoice.CarriedToInvoiceId is { } id ? targets.GetValueOrDefault(id) : null;
}
