using renting_room.Application.Billing;
using renting_room.Application.Common.Interfaces;
using renting_room.Domain.Billing;
using renting_room.Domain.Common;
using renting_room.Domain.Contracts;
using renting_room.Domain.Identity;
using renting_room.Domain.Payments;

namespace renting_room.Application.Payments;

/// <summary>
/// Giảm tiền đã thu trong transaction của người gọi (đã khóa HĐ): đảo phiếu (PM-UC-04, PM-BR-39 — kèm phiếu hoàn) và sửa số đã thu xuống
/// (PM-BR-38). "Đã thu" của mỗi hóa đơn cập nhật <b>một lần</b> theo kết quả cuối — đảo phiếu thu rồi phiếu hoàn không đi qua số âm (98 H-04).
/// Mỗi lần giảm thực thu ghi 1 <see cref="CollectionReduction"/>.
/// </summary>
public sealed class CollectionLedger(
    IPaymentRepository payments, IInvoiceRepository invoices, IInvoiceQueries invoiceQueries, PaymentPosting posting, ICurrentUser currentUser, TimeProvider clock)
{
    /// <summary>PM-BR-38 (7): quản lý khu chỉ giảm được tiền có ngày thu từ ngày 1 tháng trước.</summary>
    public bool IsLocked(DateOnly paidAt)
    {
        if (currentUser.Role != UserRole.OrgManager)
            return false;
        var today = clock.GetUtcNow().ToBusinessDate();
        return paidAt < new DateOnly(today.Year, today.Month, 1).AddMonths(-1);
    }

    /// <summary>Đảo một phiếu — trả các phiếu bị đảo (phiếu được yêu cầu đứng đầu, sau đó phiếu hoàn đảo kèm).</summary>
    public async Task<Result<IReadOnlyList<Payment>>> ReverseAsync(Guid paymentId, string reason, CancellationToken ct)
    {
        var payment = (await payments.GetWithAllocationsForUpdateAsync(paymentId, ct))!;
        if (payment.Status == PaymentStatus.Reversed)
            return PaymentErrors.AlreadyReversed;
        var parts = Active(payment).ToList();
        var locked = await invoices.LockForUpdateAsync(parts.Select(a => a.InvoiceId).Distinct().ToList(), ct);
        // BL-BR-41: nợ còn lại đã chuyển vào hóa đơn trả phòng theo số thu lúc đó — đảo thu bây giờ sẽ lệch bản kết toán.
        if (locked.Values.Any(i => i.CarriedToInvoiceId is not null))
            return BillingErrors.InvoiceCarriedOver;
        if (payment.Kind == PaymentKind.Receipt && IsLocked(payment.PaidAt))
            return PaymentErrors.CollectedPeriodLocked;

        // M-01 / PM-BR-38 (6): dòng Hoàn trả đã chốt trên phiếu sau trỏ vào hóa đơn này (BL-BR-27) — phiếu thu còn lại không đủ che ⇒ chặn
        // (đảo xong sổ vẫn ghi "đã hoàn" tiền chưa từng thu). Phải hủy phiếu chứa dòng hoàn trả trước.
        if (payment.Kind == PaymentKind.Receipt)
            foreach (var invoiceId in locked.Keys)
            {
                var refundLines = -await invoiceQueries.GetFinalizedRefundLinesTotalAsync(invoiceId, ct);
                if (refundLines > 0 && await ReceiptsLeftAsync(invoiceId, payment, ct) < refundLines)
                    return PaymentErrors.CollectedBelowRefunded(refundLines);
            }

        var before = locked.ToDictionary(i => i.Key, i => i.Value.CollectedAmount);
        var delta = locked.Keys.ToDictionary(id => id, _ => 0m);
        var now = clock.GetUtcNow();
        var touched = new List<Payment> { payment };
        payment.Reverse(reason, now);

        // Phiếu hoàn đã giảm "đã thu" ⇒ đảo nó là tăng lại; phiếu thu / bỏ nợ ⇒ giảm.
        var sign = payment.Kind == PaymentKind.Refund ? 1 : -1;
        foreach (var (invoiceId, amount) in parts)
            delta[invoiceId] += sign * amount;

        if (payment.Kind == PaymentKind.Receipt)
            foreach (var invoiceId in locked.Keys)
                touched.AddRange(await ReverseUncoveredRefundsAsync(invoiceId, payment, delta, now, ct));

        if (sign > 0 && delta.Any(d => locked[d.Key].PaidAmount + d.Value > locked[d.Key].TotalAmount))
            return PaymentErrors.ExceedsDebt;
        foreach (var (invoiceId, change) in delta)
            locked[invoiceId].ApplyPayment(change, payment.Kind == PaymentKind.WriteOff);

        if (payment.Kind == PaymentKind.Receipt)
            foreach (var (invoiceId, invoice) in locked)
                if (invoice.CollectedAmount < before[invoiceId])
                    payments.AddReduction(CollectionReduction.Record(invoice.PropertyId, invoice.ContractId, invoiceId, before[invoiceId],
                        invoice.CollectedAmount, reason));
        return touched;
    }

    /// <summary>
    /// PM-BR-39: sau khi đảo <paramref name="reversed"/>, phiếu thu còn lại của hóa đơn không đủ che các phiếu hoàn ⇒ đảo luôn phiếu hoàn
    /// (tiền chưa thu thì cũng chưa hoàn).
    /// </summary>
    private async Task<IReadOnlyList<Payment>> ReverseUncoveredRefundsAsync(
        Guid invoiceId, Payment reversed, Dictionary<Guid, decimal> delta, DateTimeOffset now, CancellationToken ct)
    {
        var refunds = (await payments.ListActiveForInvoiceAsync(invoiceId, PaymentKind.Refund, ct)).Where(p => p.Status == PaymentStatus.Recorded).ToList();
        if (refunds.Count == 0)
            return [];
        var receiptsLeft = await ReceiptsLeftAsync(invoiceId, reversed, ct);
        if (receiptsLeft >= refunds.Sum(p => AllocatedTo(p, invoiceId)))
            return [];

        foreach (var refund in refunds)
        {
            delta[invoiceId] += AllocatedTo(refund, invoiceId);
            refund.Reverse($"Đảo kèm phiếu thu {reversed.ReceiptNo}", now);
        }
        return refunds;
    }

    /// <summary>
    /// PM-BR-38 (4)(5): giảm số đã thu của <paramref name="invoice"/> (đã khóa) xuống <paramref name="target"/> — đảo phiếu thu mới nhất trước, ghi lại
    /// phần còn giữ (cùng ngày / cách thu, phân bổ vào hóa đơn khác giữ nguyên). Về 0 ⇒ đảo cả phiếu hoàn; còn &gt; 0 ⇒ giữ phiếu hoàn.
    /// </summary>
    public async Task<Result<IReadOnlyList<Payment>>> ReduceAsync(Contract contract, Invoice invoice, decimal target, string reason, CancellationToken ct)
    {
        var before = invoice.CollectedAmount;
        var reverseReason = $"Sửa đã thu: {reason.Trim()}";
        var now = clock.GetUtcNow();
        var touched = new List<Payment>();
        var change = 0m;

        var refunds = (await payments.ListActiveForInvoiceAsync(invoice.Id, PaymentKind.Refund, ct)).ToList();
        var receipts = await payments.ListActiveForInvoiceAsync(invoice.Id, PaymentKind.Receipt, ct);
        var keepRefunds = target > 0;
        var cut = receipts.Sum(p => AllocatedTo(p, invoice.Id)) - target - (keepRefunds ? refunds.Sum(p => AllocatedTo(p, invoice.Id)) : 0);

        foreach (var receipt in receipts)
        {
            if (cut <= 0)
                break;
            if (IsLocked(receipt.PaidAt))
                return PaymentErrors.CollectedPeriodLocked;
            // Phần phân bổ vào hóa đơn khác ghi lại nguyên ⇒ "đã thu" của chúng không đổi, không cần khóa thêm (HĐ đã khóa).
            var parts = Active(receipt).ToList();
            receipt.Reverse(reverseReason, now);
            touched.Add(receipt);

            var kept = new List<(Guid InvoiceId, decimal Amount)>();
            foreach (var (invoiceId, part) in parts)
            {
                var removed = invoiceId == invoice.Id ? Math.Min(cut, part) : 0;
                cut -= removed;
                change -= removed;
                if (part - removed > 0)
                    kept.Add((invoiceId, part - removed));
            }
            if (kept.Count > 0)
                touched.Add(await posting.RecordAllocatedAsync(contract, kept, receipt.Method, receipt.PaidAt, receipt.PayerName, receipt.Reference,
                    receipt.Note, PaymentKind.Receipt, ct));
        }

        if (!keepRefunds)
            foreach (var refund in refunds)
            {
                change += AllocatedTo(refund, invoice.Id);
                refund.Reverse(reverseReason, now);
                touched.Add(refund);
            }

        invoice.ApplyPayment(change);
        payments.AddReduction(CollectionReduction.Record(contract.PropertyId, contract.Id, invoice.Id, before, invoice.CollectedAmount, reason));
        return touched;
    }

    /// <summary>Tổng phiếu thu còn hiệu lực của hóa đơn nếu <paramref name="reversed"/> bị đảo.</summary>
    private async Task<decimal> ReceiptsLeftAsync(Guid invoiceId, Payment reversed, CancellationToken ct) =>
        (await payments.ListActiveForInvoiceAsync(invoiceId, PaymentKind.Receipt, ct))
        .Where(p => p.Status == PaymentStatus.Recorded && p.Id != reversed.Id).Sum(p => AllocatedTo(p, invoiceId));

    private static IEnumerable<(Guid InvoiceId, decimal Amount)> Active(Payment payment) =>
        payment.Allocations.Where(a => a.CancelledAt is null).Select(a => (a.InvoiceId, a.Amount));

    private static decimal AllocatedTo(Payment payment, Guid invoiceId) =>
        payment.Allocations.Where(a => a.CancelledAt is null && a.InvoiceId == invoiceId).Sum(a => a.Amount);
}
