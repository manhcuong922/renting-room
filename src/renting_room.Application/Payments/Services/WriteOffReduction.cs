using renting_room.Application.Billing;
using renting_room.Domain.Billing;
using renting_room.Domain.Contracts;
using renting_room.Domain.Payments;

namespace renting_room.Application.Payments;

/// <summary>
/// M08 PM-BR-37: giảm phần bỏ nợ của một hóa đơn (đã khóa HĐ + hóa đơn) — đảo phiếu bỏ nợ, ghi lại phiếu bỏ nợ phần còn lại với cùng ngày, lý do và
/// phân bổ các hóa đơn khác. Gọi sau khi đã giảm "đã thu" bằng phiếu hoàn (nếu có) để phiếu ghi lại không vượt tổng.
/// </summary>
public sealed class WriteOffReduction(IPaymentRepository payments, IInvoiceRepository invoices, PaymentPosting posting)
{
    public async Task<IReadOnlyList<Payment>> ReduceAsync(
        Contract contract, Invoice invoice, decimal amount, string reason, DateTimeOffset now, CancellationToken ct)
    {
        var touched = new List<Payment>();
        var remaining = amount;
        foreach (var writeOff in await payments.ListActiveWriteOffsForInvoiceAsync(invoice.Id, ct))
        {
            if (remaining <= 0)
                break;
            var allocations = writeOff.Allocations.Where(a => a.CancelledAt is null).Select(a => (a.InvoiceId, a.Amount)).ToList();
            var locked = await invoices.LockForUpdateAsync(allocations.Select(a => a.InvoiceId).Distinct().ToList(), ct);
            writeOff.Reverse(reason, now);
            foreach (var (id, part) in allocations)
                locked[id].ApplyPayment(-part, writeOff: true);
            touched.Add(writeOff);

            var kept = new List<(Guid InvoiceId, decimal Amount)>();
            foreach (var (id, part) in allocations)
            {
                var cut = id == invoice.Id ? Math.Min(remaining, part) : 0;
                remaining -= cut;
                if (part - cut > 0)
                    kept.Add((id, part - cut));
            }
            if (kept.Count > 0)
                touched.Add(await posting.PostAllocatedAsync(contract, locked, kept, writeOff.Method, writeOff.PaidAt, writeOff.Note,
                    PaymentKind.WriteOff, ct));
        }
        return touched;
    }
}
