using Microsoft.EntityFrameworkCore;
using renting_room.Application.Common.Interfaces;
using renting_room.Domain.Billing;

namespace renting_room.Application.Contracts;

// Thanh lý
internal static class LiquidationGuards
{
    /// <summary>CT-BR-11: còn phiếu (chưa hủy) của kỳ bắt đầu sau ngày trả phòng ⇒ phải hủy / xóa trước (tránh thu tiền kỳ sau khi đã trả phòng).</summary>
    public static Task<bool> HasInvoiceAfterAsync(IAppDbContext db, Guid contractId, DateOnly actualEndDate, CancellationToken ct) =>
        db.Invoices.AnyAsync(i => i.ContractId == contractId && i.Status != InvoiceStatus.Void && i.PeriodStart > actualEndDate, ct);
}
