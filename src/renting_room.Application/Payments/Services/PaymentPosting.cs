using renting_room.Application.Billing;
using renting_room.Application.Common.Interfaces;
using renting_room.Domain.Billing;
using renting_room.Domain.Common;
using renting_room.Domain.Contracts;
using renting_room.Domain.Payments;

namespace renting_room.Application.Payments;

/// <summary>
/// Ghi phiếu thu và phân bổ trong transaction của người gọi (đã khóa HĐ): khóa các phiếu còn nợ theo id (C-07), cập nhật <c>paid_amount</c>
/// cùng lúc (PM-BR-05). <c>amount</c> null ⇒ đúng tổng còn nợ ("Đã thu toàn bộ" / bỏ nợ khi hoàn tất thanh lý).
/// </summary>
public sealed class PaymentPosting(
    IInvoiceRepository invoices, IInvoiceQueries invoiceQueries, IPaymentRepository payments, IDocumentNumberGenerator numbers,
    ICurrentUser currentUser, TimeProvider clock)
{
    public const int MaxDaysBeforeStart = 60;

    public async Task<Result<Payment>> PostAsync(
        Contract contract, decimal? amount, PaymentMethod method, DateOnly paidAt, Guid? invoiceId, string? payerName, string? reference,
        string? note, PaymentKind kind, CancellationToken ct)
    {
        var open = await invoices.LockOpenForUpdateAsync(contract.Id, ct);

        var total = amount ?? open.Sum(i => i.Outstanding);
        var allocations = await AllocateAsync(contract.Id, total, invoiceId, open, ct);
        if (allocations.IsFailure)
            return allocations.Error!;

        var receiptNo = await numbers.NextAsync(currentUser.OrganizationId!.Value, "PT", clock.GetUtcNow().ToBusinessDate().Year, ct);
        var payment = Payment.Record(contract.PropertyId, contract.Id, receiptNo, total, method, paidAt, payerName, reference, note,
            allocations.Value!, kind);
        foreach (var (id, part) in allocations.Value!)
            open.Single(i => i.Id == id).ApplyPayment(part, kind == PaymentKind.WriteOff);
        payments.Add(payment);
        return payment;
    }

    /// <summary>M08 PM-BR-37: phiếu hoàn tiền (số PC) cho hóa đơn đã khóa — giảm "đã thu" đúng số hoàn.</summary>
    public async Task<Payment> PostRefundAsync(Contract contract, Invoice invoice, decimal amount, DateOnly paidAt, PaymentMethod method, string note, CancellationToken ct)
    {
        var receiptNo = await numbers.NextAsync(currentUser.OrganizationId!.Value, "PC", clock.GetUtcNow().ToBusinessDate().Year, ct);
        var refund = Payment.Record(contract.PropertyId, contract.Id, receiptNo, amount, method, paidAt, null, null, note, [(invoice.Id, amount)],
            PaymentKind.Refund);
        invoice.ApplyPayment(-amount);
        payments.Add(refund);
        return refund;
    }

    /// <summary>Ghi phiếu với phân bổ cho trước vào các hóa đơn đã khóa (VD ghi lại phiếu bỏ nợ sau khi giảm phần bỏ nợ).</summary>
    public async Task<Payment> PostAllocatedAsync(
        Contract contract, IReadOnlyDictionary<Guid, Invoice> locked, IReadOnlyList<(Guid InvoiceId, decimal Amount)> allocations, PaymentMethod method,
        DateOnly paidAt, string? note, PaymentKind kind, CancellationToken ct)
    {
        var receiptNo = await numbers.NextAsync(currentUser.OrganizationId!.Value, "PT", clock.GetUtcNow().ToBusinessDate().Year, ct);
        var payment = Payment.Record(contract.PropertyId, contract.Id, receiptNo, allocations.Sum(a => a.Amount), method, paidAt, null, null, note,
            allocations, kind);
        foreach (var (id, part) in allocations)
            locked[id].ApplyPayment(part, kind == PaymentKind.WriteOff);
        payments.Add(payment);
        return payment;
    }

    private async Task<Result<IReadOnlyList<(Guid InvoiceId, decimal Amount)>>> AllocateAsync(
        Guid contractId, decimal amount, Guid? invoiceId, IReadOnlyList<Invoice> open, CancellationToken ct)
    {
        if (invoiceId is { } targetId)
        {
            var target = open.FirstOrDefault(i => i.Id == targetId);
            if (target is null)
            {
                var status = await invoiceQueries.GetStatusAsync(targetId, contractId, ct);
                return status is null ? BillingErrors.NotFound
                    : status == InvoiceStatus.Finalized ? PaymentErrors.ExceedsDebt
                    : BillingErrors.NotFinalized;
            }
            return amount > target.Outstanding
                ? PaymentErrors.ExceedsDebt
                : Result.Success<IReadOnlyList<(Guid, decimal)>>([(target.Id, amount)]);
        }

        if (open.Count == 0 || amount <= 0)
            return PaymentErrors.NoDebt;
        if (amount > open.Sum(i => i.Outstanding))
            return PaymentErrors.ExceedsDebt;

        var remaining = amount;
        var result = new List<(Guid, decimal)>();
        foreach (var invoice in open.OrderBy(i => i.DueDate).ThenBy(i => i.PeriodStart))
        {
            if (remaining == 0)
                break;
            var part = Math.Min(remaining, invoice.Outstanding);
            result.Add((invoice.Id, part));
            remaining -= part;
        }
        return result;
    }
}
