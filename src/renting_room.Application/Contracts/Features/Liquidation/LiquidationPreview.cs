using Mediator;
using renting_room.Application.Billing;
using renting_room.Application.Meters;
using renting_room.Domain.Billing;
using renting_room.Domain.Common;
using renting_room.Domain.Contracts;
using renting_room.Domain.Meters;

namespace renting_room.Application.Contracts;

public sealed record LiquidationUnpaidInvoiceDto(Guid InvoiceId, string? InvoiceNo, string BillingMonth, InvoiceType Type, decimal Outstanding);

public sealed record LiquidationPreviewDto(
    ContractStatus Status, DateOnly? ActualEndDate, IReadOnlyList<LiquidationUnpaidInvoiceDto> UnpaidInvoices, decimal Outstanding, decimal RefundDue,
    IReadOnlyList<Guid> MissingFinalReadingMeterIds, Guid? FinalInvoiceId, InvoiceStatus? FinalInvoiceStatus, bool HasOtherDraftInvoices,
    int PendingRoomCharges, ContractDepositDto Deposit, IReadOnlyList<string> Blockers, bool NeedsDebtDecision, bool CanComplete);

public sealed record GetLiquidationPreviewQuery(Guid ContractId) : IRequest<Result<LiquidationPreviewDto>>;

public sealed class GetLiquidationPreviewHandler(
    IContractQueries contracts, IInvoiceQueries invoiceQueries, IRoomChargeQueries roomCharges, ContractMeterReadings meterReadings, TimeProvider clock)
    : IRequestHandler<GetLiquidationPreviewQuery, Result<LiquidationPreviewDto>>
{
    public async ValueTask<Result<LiquidationPreviewDto>> Handle(GetLiquidationPreviewQuery request, CancellationToken cancellationToken)
    {
        var contract = await contracts.GetWithRoomMovesAsync(request.ContractId, cancellationToken);
        if (contract is null)
            return ContractErrors.NotFound;

        var today = clock.GetUtcNow().ToBusinessDate();
        var invoices = await invoiceQueries.ListForBalanceAsync(contract.Id, cancellationToken);
        var unpaid = invoices.Where(i => i.Status == InvoiceStatus.Finalized && i.PaidAmount < i.TotalAmount).OrderBy(i => i.PeriodStart)
            .Select(i => new LiquidationUnpaidInvoiceDto(i.Id, i.InvoiceNo, $"{i.BillingMonth:yyyy-MM}", i.Type, i.TotalAmount - i.PaidAmount)).ToList();
        var outstanding = await invoiceQueries.GetContractOutstandingAsync(contract.Id, cancellationToken);
        var refundDue = await invoiceQueries.GetRefundDueAsync(contract.Id, cancellationToken);
        var missing = await meterReadings.MissingFinalAsync(contract, contract.ActualEndDate ?? today, cancellationToken);
        var final = invoices.FirstOrDefault(i => i.Type == InvoiceType.Final);
        var otherDrafts = invoices.Any(i => i.Status == InvoiceStatus.Draft && i.Type != InvoiceType.Final);
        var pendingCharges = await roomCharges.CountPendingAsync(contract.Id, cancellationToken);

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
