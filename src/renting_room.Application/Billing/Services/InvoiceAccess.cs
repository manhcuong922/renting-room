using renting_room.Application.Common.Interfaces;
using renting_room.Domain.Billing;
using renting_room.Domain.Common;

namespace renting_room.Application.Billing;

internal static class InvoiceAccess
{
    /// <summary>Khóa phiếu → nạp → thao tác (domain tự kiểm trạng thái) → lưu, trong 1 transaction; trả chi tiết sau khi sửa.</summary>
    public static Task<Result<InvoiceDetailDto>> MutateAsync(
        IUnitOfWork unitOfWork, IInvoiceRepository invoices, Guid invoiceId, DateOnly today, Func<Invoice, Result> action, CancellationToken ct) =>
        MutateAsync(unitOfWork, invoices, invoiceId, today, invoice => Task.FromResult(action(invoice)), ct);

    public static async Task<Result<InvoiceDetailDto>> MutateAsync(
        IUnitOfWork unitOfWork, IInvoiceRepository invoices, Guid invoiceId, DateOnly today, Func<Invoice, Task<Result>> action, CancellationToken ct)
    {
        await using var transaction = await unitOfWork.BeginTransactionAsync(ct);
        await unitOfWork.LockForUpdateAsync<Invoice>(invoiceId, ct);
        var invoice = await invoices.GetForUpdateAsync(invoiceId, ct);
        if (invoice is null)
            return BillingErrors.NotFound;

        var result = await action(invoice);
        if (result.IsFailure)
            return result.Error!;
        await unitOfWork.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return invoice.ToDetail(today);
    }
}
