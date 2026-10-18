using FluentValidation;
using Microsoft.EntityFrameworkCore;
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
internal static class PaymentPosting
{
    public const int MaxDaysBeforeStart = 60;

    /// <summary>Còn nợ của HĐ — chỉ phiếu chưa thu đủ; phiếu tổng âm (phải hoàn) không bù trừ nợ.</summary>
    public static async Task<decimal> OutstandingAsync(IAppDbContext db, Guid contractId, CancellationToken ct) =>
        await db.Invoices.Where(i => i.ContractId == contractId && i.Status == InvoiceStatus.Finalized && i.PaidAmount < i.TotalAmount)
            .SumAsync(i => (decimal?)(i.TotalAmount - i.PaidAmount), ct) ?? 0;

    /// <summary>Số chủ trọ còn phải trả lại người thuê (phiếu đã chốt tổng âm, chưa xác nhận đã hoàn — BL-BR-27).</summary>
    public static async Task<decimal> RefundDueAsync(IAppDbContext db, Guid contractId, CancellationToken ct) =>
        -(await db.Invoices.Where(i => i.ContractId == contractId && i.Status == InvoiceStatus.Finalized && i.TotalAmount < 0 && i.RefundedOn == null)
            .SumAsync(i => (decimal?)i.TotalAmount, ct) ?? 0);

    public static async Task<Result<Payment>> PostAsync(
        IAppDbContext db, IDocumentNumberGenerator numbers, ICurrentUser currentUser, TimeProvider clock, Contract contract,
        decimal? amount, PaymentMethod method, DateOnly paidAt, Guid? invoiceId, string? payerName, string? reference, string? note,
        PaymentKind kind, CancellationToken ct)
    {
        var openIds = await db.Invoices.Where(i => i.ContractId == contract.Id && i.Status == InvoiceStatus.Finalized && i.PaidAmount < i.TotalAmount)
            .Select(i => i.Id).OrderBy(id => id).ToListAsync(ct);
        foreach (var id in openIds)
            await db.LockForUpdateAsync<Invoice>(id, ct);
        var open = await db.Invoices.Where(i => openIds.Contains(i.Id)).ToListAsync(ct);

        var total = amount ?? open.Sum(i => i.Outstanding);
        var allocations = await AllocateAsync(db, contract.Id, total, invoiceId, open, ct);
        if (allocations.IsFailure)
            return allocations.Error!;

        // PM-BR-15: năm của số phiếu thu = năm của ngày lập (không phải ngày thu) — ghi bù đầu năm không làm lộn thứ tự số.
        var receiptNo = await numbers.NextAsync(currentUser.OrganizationId!.Value, "PT", clock.GetUtcNow().ToBusinessDate().Year, ct);
        var payment = Payment.Record(contract.PropertyId, contract.Id, receiptNo, total, method, paidAt, payerName, reference, note,
            allocations.Value!, kind);
        foreach (var (id, part) in allocations.Value!)
            open.Single(i => i.Id == id).ApplyPayment(part, kind == PaymentKind.WriteOff);
        db.Payments.Add(payment);
        return payment;
    }

    private static async Task<Result<IReadOnlyList<(Guid InvoiceId, decimal Amount)>>> AllocateAsync(
        IAppDbContext db, Guid contractId, decimal amount, Guid? invoiceId, List<Invoice> open, CancellationToken ct)
    {
        if (invoiceId is { } targetId)
        {
            var target = open.FirstOrDefault(i => i.Id == targetId);
            if (target is null)
            {
                var status = await db.Invoices.Where(i => i.Id == targetId && i.ContractId == contractId)
                    .Select(i => (InvoiceStatus?)i.Status).FirstOrDefaultAsync(ct);
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

        // PM-BR-06: hạn thanh toán sớm nhất trước, rồi kỳ cũ hơn.
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
