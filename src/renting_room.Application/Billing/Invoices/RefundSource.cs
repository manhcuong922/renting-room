using FluentValidation;
using Microsoft.EntityFrameworkCore;
using renting_room.Application.Common.Interfaces;
using renting_room.Domain.Billing;
using renting_room.Domain.Common;

namespace renting_room.Application.Billing;

// Sửa nháp: sửa tay, phụ thu / giảm tay, ghi chú
/// <summary>
/// BL-BR-27 (E — "chặn nhẹ"): hoàn trả ≤ số người thuê đã trả thật cho phiếu nguồn (đã thu − bỏ nợ) − các dòng hoàn trả khác đã trỏ tới phiếu
/// nguồn (phiếu chưa hủy). Gọi khi đã khóa phiếu nguồn ⇒ 2 lần hoàn song song không vượt.
/// </summary>
/// <summary>BL-BR-27: hoàn trả ≤ số đã thu thật của phiếu nguồn − các lần hoàn khác — kiểm khi thêm dòng, sửa dòng và khi chốt phiếu.</summary>
internal static class RefundSource
{
    /// <param name="excludeLineId">Dòng đang sửa — không tính vào "các lần hoàn khác".</param>
    public static async Task<Result> CheckAsync(
        IAppDbContext db, Invoice target, Guid sourceId, decimal amount, CancellationToken ct, Guid? excludeLineId = null)
    {
        var source = await db.Invoices.AsNoTracking().FirstOrDefaultAsync(i => i.Id == sourceId, ct);
        if (source is null || source.ContractId != target.ContractId || source.Status != InvoiceStatus.Finalized)
            return Result.Failure(BillingErrors.RefundSourceInvalid);
        var refunded = -(await db.Invoices.Where(i => i.Status != InvoiceStatus.Void)
            .SelectMany(i => i.Lines).Where(l => l.SourceInvoiceId == sourceId && l.Type == InvoiceLineType.Refund && l.Id != excludeLineId)
            .SumAsync(l => (decimal?)l.Amount, ct) ?? 0);
        var available = Math.Max(source.PaidAmount - source.WrittenOffAmount - refunded, 0);
        return amount > available ? Result.Failure(BillingErrors.RefundExceedsPaid(available)) : Result.Success();
    }

    /// <summary>Lúc chốt: từng dòng hoàn trả của phiếu kiểm lại theo số liệu hiện tại (phiếu nguồn có thể đã bị đảo thu sau khi thêm dòng).</summary>
    public static async Task<Result> CheckInvoiceAsync(IAppDbContext db, Invoice invoice, CancellationToken ct)
    {
        foreach (var line in invoice.Lines.Where(l => l.Type == InvoiceLineType.Refund && l.SourceInvoiceId is { } src && src != invoice.Id))
        {
            var allowed = await CheckAsync(db, invoice, line.SourceInvoiceId!.Value, Math.Abs(line.Amount), ct, line.Id);
            if (allowed.IsFailure)
                return allowed;
        }
        return Result.Success();
    }
}
