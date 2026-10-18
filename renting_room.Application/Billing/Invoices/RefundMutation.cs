using FluentValidation;
using Microsoft.EntityFrameworkCore;
using renting_room.Application.Common.Interfaces;
using renting_room.Domain.Billing;
using renting_room.Domain.Common;
using renting_room.Domain.Contracts;
using renting_room.Domain.Payments;

namespace renting_room.Application.Billing;

internal static class RefundMutation
{
    /// <summary>
    /// Khóa HĐ → phiếu (C-07, cùng thứ tự với hoàn tất thanh lý) rồi thao tác: hoàn tất thanh lý đọc số phải hoàn dưới khóa HĐ nên
    /// không lọt giữa chừng; HĐ đã kết thúc thì không đổi trạng thái hoàn tiền nữa.
    /// </summary>
    public static async Task<Result<InvoiceDetailDto>> RunAsync(
        IAppDbContext db, Guid invoiceId, DateOnly today, Func<Invoice, Result> action, CancellationToken ct)
    {
        var contractId = await db.Invoices.Where(i => i.Id == invoiceId).Select(i => (Guid?)i.ContractId).FirstOrDefaultAsync(ct);
        if (contractId is null)
            return BillingErrors.NotFound;

        await using var transaction = await db.BeginTransactionAsync(ct);
        await db.LockForUpdateAsync<Contract>(contractId.Value, ct);
        if (await db.Contracts.AnyAsync(c => c.Id == contractId && c.Status == ContractStatus.Ended, ct))
            return PaymentErrors.ContractNotBillable;
        await db.LockForUpdateAsync<Invoice>(invoiceId, ct);
        var invoice = (await InvoiceAccess.LoadAsync(db, invoiceId, ct))!;

        var result = action(invoice);
        if (result.IsFailure)
            return result.Error!;
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return invoice.ToDetail(today);
    }
}
