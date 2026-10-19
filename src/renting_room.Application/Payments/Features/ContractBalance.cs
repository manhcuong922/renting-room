using Mediator;
using renting_room.Application.Billing;
using renting_room.Application.Contracts;
using renting_room.Domain.Billing;
using renting_room.Domain.Common;
using renting_room.Domain.Contracts;

namespace renting_room.Application.Payments;

/// <param name="PaidAmount">Đã xử lý = thực thu + bỏ nợ (PM-BR-36).</param>
/// <param name="Outstanding">Còn phải thu của phiếu này.</param>
/// <param name="IsOverdue">Đã quá hạn thanh toán mà chưa thu đủ.</param>
/// <param name="CollectedAmount">Thực thu — tiền người thuê đã trả thật.</param>
/// <param name="WrittenOffAmount">Phần đã bỏ nợ.</param>
public sealed record ContractUnpaidInvoiceDto(
    Guid InvoiceId, string? InvoiceNo, string BillingMonth, InvoiceType Type, decimal TotalAmount, decimal PaidAmount, decimal Outstanding,
    DateOnly? DueDate, bool IsOverdue, decimal CollectedAmount = 0, decimal WrittenOffAmount = 0);

/// <param name="Outstanding">Tổng còn nợ của HĐ = tổng phần chưa thu của các phiếu đã chốt — số tối đa thu được khi "thu theo hợp đồng".</param>
/// <param name="OverdueAmount">Phần còn nợ đã quá hạn.</param>
/// <param name="RefundDue">Số chủ trọ còn phải trả lại người thuê (phiếu tổng âm chưa xác nhận hoàn) — không bù trừ vào nợ.</param>
/// <param name="DraftInvoices">Số phiếu nháp chưa chốt (chưa tính vào nợ).</param>
/// <param name="PendingRoomCharges">Số khoản phát sinh chưa lên phiếu.</param>
public sealed record ContractBalanceDto(
    Guid ContractId, string ContractNo, ContractStatus Status, decimal Outstanding, decimal OverdueAmount, decimal RefundDue,
    IReadOnlyList<ContractUnpaidInvoiceDto> UnpaidInvoices, int DraftInvoices, int PendingRoomCharges);

public sealed record GetContractBalanceQuery(Guid ContractId) : IRequest<Result<ContractBalanceDto>>;

/// <summary>PM-UC-09: số nợ của một hợp đồng cho màn "thu theo hợp đồng" — cùng cách tính với giới hạn của lệnh ghi thu (PM-BR-03).</summary>
public sealed class GetContractBalanceHandler(
    IContractQueries contracts, IInvoiceQueries invoiceQueries, IRoomChargeQueries roomCharges, TimeProvider clock)
    : IRequestHandler<GetContractBalanceQuery, Result<ContractBalanceDto>>
{
    public async ValueTask<Result<ContractBalanceDto>> Handle(GetContractBalanceQuery request, CancellationToken cancellationToken)
    {
        var contract = await contracts.GetHeaderAsync(request.ContractId, cancellationToken);
        if (contract is null)
            return ContractErrors.NotFound;

        var today = clock.GetUtcNow().ToBusinessDate();
        var invoices = await invoiceQueries.ListForBalanceAsync(contract.Id, cancellationToken);
        var unpaid = invoices.Where(i => i.Status == InvoiceStatus.Finalized && i.PaidAmount < i.TotalAmount)
            .Select(i => new ContractUnpaidInvoiceDto(i.Id, i.InvoiceNo, $"{i.BillingMonth:yyyy-MM}", i.Type, i.TotalAmount, i.PaidAmount,
                i.TotalAmount - i.PaidAmount, i.DueDate, i.DueDate < today, i.PaidAmount - i.WrittenOffAmount, i.WrittenOffAmount))
            .ToList();
        var pendingCharges = await roomCharges.CountPendingAsync(contract.Id, cancellationToken);

        return new ContractBalanceDto(
            contract.Id, contract.ContractNo, contract.Status, unpaid.Sum(i => i.Outstanding), unpaid.Where(i => i.IsOverdue).Sum(i => i.Outstanding),
            await invoiceQueries.GetRefundDueAsync(contract.Id, cancellationToken), unpaid,
            invoices.Count(i => i.Status == InvoiceStatus.Draft), pendingCharges);
    }
}
