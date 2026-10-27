using renting_room.Application.Billing;
using renting_room.Application.Common.Interfaces;
using renting_room.Domain.Billing;
using renting_room.Domain.Contracts;

namespace renting_room.Application.Contracts;

/// <summary>
/// M07 BL-BR-41: cọc đổi (không trả cọc / bỏ đánh dấu / đã hoàn) khi đang thanh lý ⇒ nháp hóa đơn trả phòng tự tính lại dòng "Trừ tiền cọc",
/// giữ ô sửa tay. Gọi trong transaction đã khóa HĐ (C-07).
/// </summary>
public sealed class FinalDraftRefresher(IInvoiceRepository invoices, DraftRecalculator recalculator, IUnitOfWork unitOfWork)
{
    public async Task RefreshAsync(Contract contract, CancellationToken ct)
    {
        if (contract.Status != ContractStatus.Liquidating
            || await invoices.FindFinalForUpdateAsync(contract.Id, ct) is not { Status: InvoiceStatus.Draft } final)
            return;
        await unitOfWork.LockForUpdateAsync<Invoice>(final.Id, ct);
        await recalculator.RecalculateAsync(contract.Id, final.Id, keepManualEdits: true, ct);
    }
}
