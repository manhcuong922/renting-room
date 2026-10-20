using renting_room.Application.Billing;
using renting_room.Application.Common.Interfaces;
using renting_room.Domain.Billing;

namespace renting_room.Application.Contracts;

/// <summary>
/// M05 CT-BR-11 / CT-UC-08: nháp hóa đơn tháng của kỳ bắt đầu sau ngày trả phòng là kỳ ảo ⇒ xóa khi bắt đầu thanh lý / ký lại (khoản phát sinh
/// trên nháp về chờ). Gọi trong transaction đã khóa HĐ — khóa tiếp các nháp theo id (C-07).
/// </summary>
public sealed class LiquidationDrafts(IInvoiceRepository invoices, RoomChargeBilling roomCharges, IUnitOfWork unitOfWork)
{
    public async Task DeleteAfterAsync(Guid contractId, DateOnly actualEnd, CancellationToken ct)
    {
        var drafts = (await invoices.ListWithDetailsByContractAsync(contractId, ct))
            .Where(i => i is { Status: InvoiceStatus.Draft, Type: InvoiceType.Regular } && i.PeriodStart > actualEnd).ToList();
        if (drafts.Count == 0)
            return;
        foreach (var draft in drafts.OrderBy(d => d.Id))
            await unitOfWork.LockForUpdateAsync<Invoice>(draft.Id, ct);
        foreach (var draft in drafts)
        {
            await roomCharges.DetachAllAsync(draft.Id, ct);
            invoices.Remove(draft);
        }
        await unitOfWork.SaveChangesAsync(ct);
    }
}
