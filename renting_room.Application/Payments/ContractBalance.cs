using Mediator;
using Microsoft.EntityFrameworkCore;
using renting_room.Application.Common.Interfaces;
using renting_room.Domain.Billing;
using renting_room.Domain.Common;
using renting_room.Domain.Contracts;

namespace renting_room.Application.Payments;

/// <param name="Outstanding">Còn phải thu của phiếu này.</param>
/// <param name="IsOverdue">Đã quá hạn thanh toán mà chưa thu đủ.</param>
public sealed record ContractUnpaidInvoiceDto(
    Guid InvoiceId, string? InvoiceNo, string BillingMonth, InvoiceType Type, decimal TotalAmount, decimal PaidAmount, decimal Outstanding,
    DateOnly? DueDate, bool IsOverdue);

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
public sealed class GetContractBalanceHandler(IAppDbContext db, TimeProvider clock) : IRequestHandler<GetContractBalanceQuery, Result<ContractBalanceDto>>
{
    public async ValueTask<Result<ContractBalanceDto>> Handle(GetContractBalanceQuery request, CancellationToken cancellationToken)
    {
        var contract = await db.Contracts.AsNoTracking().Where(c => c.Id == request.ContractId)
            .Select(c => new { c.Id, c.ContractNo, c.Status }).FirstOrDefaultAsync(cancellationToken);
        if (contract is null)
            return ContractErrors.NotFound;

        var today = clock.GetUtcNow().ToBusinessDate();
        var invoices = await db.Invoices.AsNoTracking().Where(i => i.ContractId == contract.Id && i.Status != InvoiceStatus.Void)
            .OrderBy(i => i.PeriodStart)
            .Select(i => new { i.Id, i.InvoiceNo, i.BillingMonth, i.Type, i.Status, i.TotalAmount, i.PaidAmount, i.DueDate })
            .ToListAsync(cancellationToken);
        var unpaid = invoices.Where(i => i.Status == InvoiceStatus.Finalized && i.PaidAmount < i.TotalAmount)
            .Select(i => new ContractUnpaidInvoiceDto(i.Id, i.InvoiceNo, $"{i.BillingMonth:yyyy-MM}", i.Type, i.TotalAmount, i.PaidAmount,
                i.TotalAmount - i.PaidAmount, i.DueDate, i.DueDate < today))
            .ToList();
        var pendingCharges = await db.RoomCharges.CountAsync(c => c.ContractId == contract.Id && c.InvoiceId == null && c.CancelledAt == null,
            cancellationToken);

        return new ContractBalanceDto(
            contract.Id, contract.ContractNo, contract.Status, unpaid.Sum(i => i.Outstanding), unpaid.Where(i => i.IsOverdue).Sum(i => i.Outstanding),
            await PaymentPosting.RefundDueAsync(db, contract.Id, cancellationToken), unpaid,
            invoices.Count(i => i.Status == InvoiceStatus.Draft), pendingCharges);
    }
}
