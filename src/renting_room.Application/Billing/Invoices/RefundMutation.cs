using renting_room.Application.Common.Interfaces;
using renting_room.Application.Contracts;
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
        IUnitOfWork unitOfWork, IInvoiceRepository invoices, IInvoiceQueries invoiceQueries, IContractQueries contracts, Guid invoiceId,
        DateOnly today, Func<Invoice, Result> action, CancellationToken ct)
    {
        var contractId = await invoiceQueries.GetContractIdAsync(invoiceId, ct);
        if (contractId is null)
            return BillingErrors.NotFound;

        await using var transaction = await unitOfWork.BeginTransactionAsync(ct);
        await unitOfWork.LockForUpdateAsync<Contract>(contractId.Value, ct);
        if (await contracts.GetStatusAsync(contractId.Value, ct) == ContractStatus.Ended)
            return PaymentErrors.ContractNotBillable;
        await unitOfWork.LockForUpdateAsync<Invoice>(invoiceId, ct);
        var invoice = (await invoices.GetForUpdateAsync(invoiceId, ct))!;

        var result = action(invoice);
        if (result.IsFailure)
            return result.Error!;
        await unitOfWork.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return invoice.ToDetail(today);
    }
}
