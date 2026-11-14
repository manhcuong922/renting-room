using renting_room.Application.Common.Interfaces;
using renting_room.Domain.Billing;
using renting_room.Domain.Common;

namespace renting_room.Application.Billing;

internal static class InvoiceAccess
{
    /// <summary>
    /// M07 §10 (05/11/2026): thao tác trên một phiếu kèm version người dùng đang thấy — lệch ⇒ 409 (đọc sau khi đã khóa hàng phiếu nên version chắc chắn mới nhất).
    /// Null = lệnh nội bộ / hàng loạt, không kiểm.
    /// </summary>
    public static Error? StaleVersion(Invoice invoice, uint? expectedVersion) =>
        expectedVersion is { } expected && invoice.Version != expected ? BillingErrors.ConcurrencyConflict : null;

    /// <summary>Khóa phiếu → nạp → thao tác (domain tự kiểm trạng thái) → lưu, trong 1 transaction; trả chi tiết sau khi sửa.</summary>
    public static Task<Result<InvoiceDetailDto>> MutateAsync(
        IUnitOfWork unitOfWork, IInvoiceRepository invoices, IInvoiceQueries queries, Guid invoiceId, DateOnly today, uint? expectedVersion,
        Func<Invoice, Result> action, CancellationToken ct) =>
        MutateAsync(unitOfWork, invoices, queries, invoiceId, today, expectedVersion, invoice => Task.FromResult(action(invoice)), ct);

    public static async Task<Result<InvoiceDetailDto>> MutateAsync(
        IUnitOfWork unitOfWork, IInvoiceRepository invoices, IInvoiceQueries queries, Guid invoiceId, DateOnly today, uint? expectedVersion,
        Func<Invoice, Task<Result>> action, CancellationToken ct)
    {
        await using var transaction = await unitOfWork.BeginTransactionAsync(ct);
        await unitOfWork.LockForUpdateAsync<Invoice>(invoiceId, ct);
        var invoice = await invoices.GetForUpdateAsync(invoiceId, ct);
        if (invoice is null)
            return BillingErrors.NotFound;
        if (StaleVersion(invoice, expectedVersion) is { } stale)
            return stale;

        var result = await action(invoice);
        if (result.IsFailure)
            return result.Error!;
        await unitOfWork.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return invoice.ToDetail(today, await queries.LoadAsync(invoice, today, ct));
    }
}
