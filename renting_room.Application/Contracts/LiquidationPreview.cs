using Mediator;
using Microsoft.EntityFrameworkCore;
using renting_room.Application.Common.Interfaces;
using renting_room.Application.Meters;
using renting_room.Application.Payments;
using renting_room.Domain.Billing;
using renting_room.Domain.Common;
using renting_room.Domain.Contracts;
using renting_room.Domain.Meters;

namespace renting_room.Application.Contracts;

public sealed record LiquidationUnpaidInvoiceDto(Guid InvoiceId, string? InvoiceNo, string BillingMonth, InvoiceType Type, decimal Outstanding);

/// <param name="FinalInvoiceStatus">null = chưa lập phiếu quyết toán.</param>
/// <param name="Blockers">Các mã sẽ chặn "hoàn tất thanh lý" nếu bấm ngay bây giờ (CT-BR-12).</param>
/// <param name="NeedsDebtDecision">Còn nợ ⇒ khi hoàn tất phải chọn "đã thu toàn bộ" hoặc "bỏ nợ".</param>
public sealed record LiquidationPreviewDto(
    ContractStatus Status, DateOnly? ActualEndDate, IReadOnlyList<LiquidationUnpaidInvoiceDto> UnpaidInvoices, decimal Outstanding, decimal RefundDue,
    IReadOnlyList<Guid> MissingFinalReadingMeterIds, Guid? FinalInvoiceId, InvoiceStatus? FinalInvoiceStatus, bool HasOtherDraftInvoices,
    int PendingRoomCharges, ContractDepositDto Deposit, IReadOnlyList<string> Blockers, bool NeedsDebtDecision, bool CanComplete);

public sealed record GetLiquidationPreviewQuery(Guid ContractId) : IRequest<Result<LiquidationPreviewDto>>;

/// <summary>
/// CT-UC-08: tổng hợp một lần những gì cần xem trước khi hoàn tất thanh lý — cùng các điều kiện mà lệnh hoàn tất kiểm (CT-BR-12), chỉ đọc.
/// </summary>
public sealed class GetLiquidationPreviewHandler(IAppDbContext db, TimeProvider clock)
    : IRequestHandler<GetLiquidationPreviewQuery, Result<LiquidationPreviewDto>>
{
    public async ValueTask<Result<LiquidationPreviewDto>> Handle(GetLiquidationPreviewQuery request, CancellationToken cancellationToken)
    {
        var contract = await db.Contracts.AsNoTracking().Include(c => c.RoomMoves).FirstOrDefaultAsync(c => c.Id == request.ContractId, cancellationToken);
        if (contract is null)
            return ContractErrors.NotFound;

        var today = clock.GetUtcNow().ToBusinessDate();
        var invoices = await db.Invoices.AsNoTracking().Where(i => i.ContractId == contract.Id && i.Status != InvoiceStatus.Void)
            .Select(i => new { i.Id, i.InvoiceNo, i.BillingMonth, i.Type, i.Status, i.TotalAmount, i.PaidAmount, i.PeriodStart }).ToListAsync(cancellationToken);
        var unpaid = invoices.Where(i => i.Status == InvoiceStatus.Finalized && i.PaidAmount < i.TotalAmount).OrderBy(i => i.PeriodStart)
            .Select(i => new LiquidationUnpaidInvoiceDto(i.Id, i.InvoiceNo, $"{i.BillingMonth:yyyy-MM}", i.Type, i.TotalAmount - i.PaidAmount)).ToList();
        var outstanding = await PaymentPosting.OutstandingAsync(db, contract.Id, cancellationToken);
        var refundDue = await PaymentPosting.RefundDueAsync(db, contract.Id, cancellationToken);
        var missing = await ContractMeterReadings.MissingFinalAsync(db, contract, contract.ActualEndDate ?? today, cancellationToken);
        var final = invoices.FirstOrDefault(i => i.Type == InvoiceType.Final);
        var otherDrafts = invoices.Any(i => i.Status == InvoiceStatus.Draft && i.Type != InvoiceType.Final);
        var pendingCharges = await db.RoomCharges.CountAsync(c => c.ContractId == contract.Id && c.InvoiceId == null && c.CancelledAt == null, cancellationToken);

        var blockers = new List<string>();
        if (contract.Status != ContractStatus.Liquidating)
            blockers.Add(ContractErrors.NotLiquidating.Code);
        else if (today < contract.ActualEndDate)
            blockers.Add(ContractErrors.LiquidationBeforeEndDate.Code);
        if (missing.Count > 0)
            blockers.Add(MeterErrors.FinalReadingRequired(missing).Code);
        if (invoices.Any(i => i.Status == InvoiceStatus.Draft))
            blockers.Add(BillingErrors.DraftExists.Code);
        if (final is not { Status: InvoiceStatus.Finalized })
            blockers.Add(BillingErrors.FinalRequired.Code);
        if (refundDue > 0)
            blockers.Add(BillingErrors.RefundPending(refundDue).Code);

        return new LiquidationPreviewDto(
            contract.Status, contract.ActualEndDate, unpaid, outstanding, refundDue, missing, final?.Id, final?.Status, otherDrafts, pendingCharges,
            new ContractDepositDto(contract.DepositAmount, contract.HasDeposit ? contract.DepositStatus : null, contract.DepositRefundedOn,
                contract.DepositRefundedAmount, contract.DepositNote,
                contract.Status == ContractStatus.Ended && contract.HasDeposit && contract.DepositStatus == DepositStatus.Holding),
            blockers.Distinct().ToList(), outstanding > 0, blockers.Count == 0);
    }
}
