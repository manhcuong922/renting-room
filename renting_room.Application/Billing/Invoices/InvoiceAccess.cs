using Microsoft.EntityFrameworkCore;
using renting_room.Application.Common.Interfaces;
using renting_room.Domain.Billing;
using renting_room.Domain.Common;

namespace renting_room.Application.Billing;

// DTO
internal static class InvoiceAccess
{
    public static Task<Invoice?> LoadAsync(IAppDbContext db, Guid id, CancellationToken ct) =>
        db.Invoices.Include(i => i.Lines).Include(i => i.Segments).AsSplitQuery().FirstOrDefaultAsync(i => i.Id == id, ct);

    /// <summary>Khóa phiếu → nạp → thao tác (domain tự kiểm trạng thái) → lưu, trong 1 transaction; trả chi tiết sau khi sửa.</summary>
    public static Task<Result<InvoiceDetailDto>> MutateAsync(
        IAppDbContext db, Guid invoiceId, DateOnly today, Func<Invoice, Result> action, CancellationToken ct) =>
        MutateAsync(db, invoiceId, today, invoice => Task.FromResult(action(invoice)), ct);

    public static async Task<Result<InvoiceDetailDto>> MutateAsync(
        IAppDbContext db, Guid invoiceId, DateOnly today, Func<Invoice, Task<Result>> action, CancellationToken ct)
    {
        await using var transaction = await db.BeginTransactionAsync(ct);
        await db.LockForUpdateAsync<Invoice>(invoiceId, ct);
        var invoice = await LoadAsync(db, invoiceId, ct);
        if (invoice is null)
            return BillingErrors.NotFound;

        var result = await action(invoice);
        if (result.IsFailure)
            return result.Error!;
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return invoice.ToDetail(today);
    }

    /// <summary>
    /// BL-BR-22: chỉ hủy / xóa phiếu mới nhất chưa hủy của HĐ. Phiếu quyết toán cùng kỳ với phiếu thường của kỳ cuối nhưng luôn là mới nhất —
    /// hủy phiếu thường kỳ cuối khi còn phiếu quyết toán sẽ làm mất tiền phòng / dịch vụ kỳ cuối.
    /// </summary>
    public static Task<bool> HasLaterInvoiceAsync(IAppDbContext db, Invoice invoice, CancellationToken ct) =>
        db.Invoices.AnyAsync(i => i.ContractId == invoice.ContractId && i.Status != InvoiceStatus.Void && i.Id != invoice.Id
            && (i.PeriodStart > invoice.PeriodStart
                || (invoice.Type == InvoiceType.Regular && i.Type == InvoiceType.Final && i.PeriodStart == invoice.PeriodStart)), ct);
}
