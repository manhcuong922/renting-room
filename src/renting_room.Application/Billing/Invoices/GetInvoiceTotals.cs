using FluentValidation;
using Mediator;
using Microsoft.EntityFrameworkCore;
using renting_room.Application.Common.Interfaces;
using renting_room.Domain.Billing;
using renting_room.Domain.Common;

namespace renting_room.Application.Billing;

// Truy vấn
/// <param name="TotalAmount">Tổng phải thu của phiếu đã chốt (phiếu tổng âm — chờ hoàn — không tính).</param>
/// <param name="OverdueAmount">Phần còn nợ của phiếu đã quá hạn thanh toán.</param>
/// <param name="DraftAmount">Tổng tạm tính của phiếu nháp — chưa phải số phải thu.</param>
/// <param name="PaidAmount">Đã xử lý = thực thu + bỏ nợ (PM-BR-36); <paramref name="CollectedAmount"/> = thực thu; <paramref name="WrittenOffAmount"/> = đã bỏ nợ.</param>
public sealed record InvoiceTotalsDto(
    int FinalizedCount, decimal TotalAmount, decimal PaidAmount, decimal OutstandingAmount, decimal OverdueAmount, int DraftCount, decimal DraftAmount,
    decimal CollectedAmount = 0, decimal WrittenOffAmount = 0);

/// <summary>BL-UC-02: số tổng hợp trên toàn bộ kết quả lọc (không riêng trang đang xem).</summary>
public sealed record GetInvoiceTotalsQuery(ListInvoicesQuery Filter) : IRequest<InvoiceTotalsDto>;

public sealed class GetInvoiceTotalsHandler(IAppDbContext db, TimeProvider clock) : IRequestHandler<GetInvoiceTotalsQuery, InvoiceTotalsDto>
{
    public async ValueTask<InvoiceTotalsDto> Handle(GetInvoiceTotalsQuery request, CancellationToken cancellationToken)
    {
        var today = clock.GetUtcNow().ToBusinessDate();
        var query = InvoiceFilters.Apply(db, request.Filter);
        var finalized = await query.Where(i => i.Status == InvoiceStatus.Finalized && i.TotalAmount > 0)
            .GroupBy(_ => 1)
            .Select(g => new
            {
                Count = g.Count(), Total = g.Sum(i => i.TotalAmount), Paid = g.Sum(i => i.PaidAmount), WrittenOff = g.Sum(i => i.WrittenOffAmount),
                Outstanding = g.Sum(i => i.PaidAmount < i.TotalAmount ? i.TotalAmount - i.PaidAmount : 0),
                Overdue = g.Sum(i => i.PaidAmount < i.TotalAmount && i.DueDate < today ? i.TotalAmount - i.PaidAmount : 0)
            })
            .FirstOrDefaultAsync(cancellationToken);
        var drafts = await query.Where(i => i.Status == InvoiceStatus.Draft).GroupBy(_ => 1)
            .Select(g => new { Count = g.Count(), Total = g.Sum(i => i.TotalAmount) }).FirstOrDefaultAsync(cancellationToken);
        var paid = finalized?.Paid ?? 0;
        var writtenOff = finalized?.WrittenOff ?? 0;
        return new InvoiceTotalsDto(finalized?.Count ?? 0, finalized?.Total ?? 0, paid, finalized?.Outstanding ?? 0,
            finalized?.Overdue ?? 0, drafts?.Count ?? 0, drafts?.Total ?? 0, paid - writtenOff, writtenOff);
    }
}
