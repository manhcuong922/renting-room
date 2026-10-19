using Mediator;
using renting_room.Domain.Common;

namespace renting_room.Application.Billing;

/// <param name="TotalAmount">Tổng phải thu của phiếu đã chốt (phiếu tổng âm — chờ hoàn — không tính).</param>
/// <param name="OverdueAmount">Phần còn nợ của phiếu đã quá hạn thanh toán.</param>
/// <param name="DraftAmount">Tổng tạm tính của phiếu nháp — chưa phải số phải thu.</param>
/// <param name="PaidAmount">Đã xử lý = thực thu + bỏ nợ (PM-BR-36); <paramref name="CollectedAmount"/> = thực thu; <paramref name="WrittenOffAmount"/> = đã bỏ nợ.</param>
public sealed record InvoiceTotalsDto(
    int FinalizedCount, decimal TotalAmount, decimal PaidAmount, decimal OutstandingAmount, decimal OverdueAmount, int DraftCount, decimal DraftAmount,
    decimal CollectedAmount = 0, decimal WrittenOffAmount = 0);

/// <summary>BL-UC-02: số tổng hợp trên toàn bộ kết quả lọc (không riêng trang đang xem).</summary>
public sealed record GetInvoiceTotalsQuery(ListInvoicesQuery Filter) : IRequest<InvoiceTotalsDto>;

public sealed class GetInvoiceTotalsHandler(IInvoiceQueries invoices, TimeProvider clock) : IRequestHandler<GetInvoiceTotalsQuery, InvoiceTotalsDto>
{
    public async ValueTask<InvoiceTotalsDto> Handle(GetInvoiceTotalsQuery request, CancellationToken cancellationToken) =>
        await invoices.GetTotalsAsync(request.Filter, clock.GetUtcNow().ToBusinessDate(), cancellationToken);
}
