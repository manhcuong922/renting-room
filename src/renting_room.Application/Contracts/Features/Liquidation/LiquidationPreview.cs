using Mediator;
using renting_room.Application.Billing;
using renting_room.Application.Meters;
using renting_room.Application.Properties;
using renting_room.Domain.Billing;
using renting_room.Domain.Common;
using renting_room.Domain.Contracts;
using renting_room.Domain.Meters;

namespace renting_room.Application.Contracts;

public sealed record LiquidationUnpaidInvoiceDto(Guid InvoiceId, string? InvoiceNo, string BillingMonth, InvoiceType Type, decimal Outstanding);

/// <param name="Days">Số ngày ở trong kỳ cuối; <paramref name="PeriodDays"/> = số ngày của kỳ chuẩn.</param>
public sealed record LiquidationFinalPeriodDto(DateOnly Start, DateOnly End, int Days, int PeriodDays);

public sealed record LiquidationPeriodDto(DateOnly Start, DateOnly End);

/// <summary>Số tiền phòng + dịch vụ kỳ cuối theo một cách tính (CT-BR-11) — điện nước không phụ thuộc lựa chọn.</summary>
public sealed record FinalPeriodOptionDto(FinalPeriodCharge Charge, decimal Rent, decimal Services);

public sealed record LiquidationPreviewDto(
    ContractStatus Status, DateOnly? ActualEndDate, IReadOnlyList<LiquidationUnpaidInvoiceDto> UnpaidInvoices, decimal Outstanding, decimal RefundDue,
    IReadOnlyList<Guid> MissingFinalReadingMeterIds, Guid? FinalInvoiceId, InvoiceStatus? FinalInvoiceStatus, bool HasOtherDraftInvoices,
    int PendingRoomCharges, ContractDepositDto Deposit, IReadOnlyList<string> Blockers, bool NeedsDebtDecision, bool CanComplete,
    LiquidationFinalPeriodDto? FinalPeriod = null, FinalPeriodCharge FinalPeriodCharge = FinalPeriodCharge.FullPeriod,
    IReadOnlyList<FinalPeriodOptionDto>? FinalPeriodOptions = null, bool FinalInvoiceRequired = true, DateOnly? LastBilledEnd = null,
    IReadOnlyList<LiquidationPeriodDto>? UnbilledPeriods = null,
    IReadOnlyList<InvoiceFromEndRow>? InvoicesFromEndDate = null, IReadOnlyList<RoomChargeDto>? PendingCharges = null,
    IReadOnlyList<Guid>? PendingChargeIdsAfterEndDate = null,
    bool NeedsRefundDecision = false, bool NeedsChargesDecision = false);

/// <param name="ActualEndDate">HĐ còn hiệu lực: ngày trả phòng định chọn (mặc định ngày dự kiến trả đã báo, không có thì hôm nay).</param>
public sealed record GetLiquidationPreviewQuery(Guid ContractId, DateOnly? ActualEndDate = null) : IRequest<Result<LiquidationPreviewDto>>;

/// <summary>M05 CT-UC-08: màn thanh lý — mọi mục cần chủ trọ quyết trước / khi hoàn tất (CT-BR-11, CT-BR-12).</summary>
public sealed class GetLiquidationPreviewHandler(
    IContractQueries contracts, IInvoiceQueries invoiceQueries, IRoomChargeRepository roomChargeRepository, RoomChargeBilling roomCharges,
    IPropertyQueries properties, ContractMeterReadings meterReadings, InvoiceInputLoader inputs, TimeProvider clock)
    : IRequestHandler<GetLiquidationPreviewQuery, Result<LiquidationPreviewDto>>
{
    public async ValueTask<Result<LiquidationPreviewDto>> Handle(GetLiquidationPreviewQuery request, CancellationToken cancellationToken)
    {
        var contract = await contracts.GetDetailsAsync(request.ContractId, cancellationToken);
        if (contract is null)
            return ContractErrors.NotFound;

        var today = clock.GetUtcNow().ToBusinessDate();
        var endDate = contract.ActualEndDate ?? request.ActualEndDate ?? contract.PlannedMoveOutDate ?? today;
        var invoices = await invoiceQueries.ListForBalanceAsync(contract.Id, cancellationToken);
        var unpaid = invoices.Where(i => i.Status == InvoiceStatus.Finalized && i.PaidAmount < i.TotalAmount).OrderBy(i => i.PeriodStart)
            .Select(i => new LiquidationUnpaidInvoiceDto(i.Id, i.InvoiceNo, $"{i.BillingMonth:yyyy-MM}", i.Type, i.TotalAmount - i.PaidAmount)).ToList();
        var outstanding = await invoiceQueries.GetContractOutstandingAsync(contract.Id, cancellationToken);
        var refundDue = await invoiceQueries.GetRefundDueAsync(contract.Id, cancellationToken);
        var missing = await meterReadings.MissingFinalAsync(contract, endDate, cancellationToken);
        var final = invoices.FirstOrDefault(i => i.Type == InvoiceType.Final);
        var otherDrafts = invoices.Any(i => i.Status == InvoiceStatus.Draft && i.Type != InvoiceType.Final);
        var pending = (await roomChargeRepository.ListPendingByContractAsync(contract.Id, cancellationToken)).Where(c => !c.IsSettled).ToList();

        var billing = await properties.GetBillingAsync(contract.PropertyId, cancellationToken);
        var periods = endDate < contract.BillingStartDate ? [] : billing.Schedule.ContractPeriods(contract.BillingStartDate, endDate, endDate);
        var last = periods.Count > 0 ? periods[^1] : null;
        // Kỳ đã qua chưa chốt hóa đơn (chưa có / chỉ có nháp) ⇒ "Tạo hóa đơn" hoặc "Coi như trả phòng từ …" (CT-UC-08).
        var unbilled = periods.Where(p => p != last && !invoices.Any(i => i.PeriodStart == p.Start && i.Status == InvoiceStatus.Finalized))
            .Select(p => new LiquidationPeriodDto(p.Start, p.End)).ToList();
        var periodRows = await invoiceQueries.ListPeriodsAsync(contract.Id, cancellationToken);
        var finalRequired = FinalInvoiceNeed.IsRequired(billing.Schedule, contract.BillingStartDate, endDate, periodRows);
        var options = last is null ? [] : await OptionsAsync(contract, last, cancellationToken);

        var blockers = Blockers(contract, today, finalRequired ? missing : [], invoices, finalRequired ? final : null, finalRequired);
        return new LiquidationPreviewDto(
            contract.Status, contract.ActualEndDate, unpaid, outstanding, refundDue, finalRequired ? missing : [], final?.Id, final?.Status, otherDrafts,
            pending.Count,
            new ContractDepositDto(contract.DepositAmount, contract.HasDeposit ? contract.DepositStatus : null, contract.DepositRefundedOn,
                contract.DepositRefundedAmount, contract.DepositNote,
                contract.Status == ContractStatus.Ended && contract.HasDeposit && contract.DepositStatus == DepositStatus.Holding),
            blockers, outstanding > 0, blockers.Count == 0,
            last is null ? null : new LiquidationFinalPeriodDto(last.Start, last.End, last.End.DayNumber - last.Start.DayNumber + 1,
                billing.Schedule.StandardPeriodContaining(last.Start).Days),
            contract.FinalPeriodCharge ?? FinalPeriodCharge.FullPeriod, options, finalRequired, FinalInvoiceNeed.LastBilledEnd(periodRows), unbilled,
            await invoiceQueries.ListRegularEndingAfterAsync(contract.Id, endDate, cancellationToken),
            await roomCharges.ToDtosAsync(pending, cancellationToken),
            // Phát sinh sau ngày trả phòng (thường từ nháp kỳ ảo bị xóa) — chủ trọ xem lại / hủy từng khoản trước khi hoàn tất.
            pending.Where(c => !c.IsReadingAdjustment && c.IncurredOn > endDate).Select(c => c.Id).ToList(), refundDue > 0, pending.Count > 0);
    }

    private async Task<IReadOnlyList<FinalPeriodOptionDto>> OptionsAsync(Contract contract, BillingPeriod last, CancellationToken ct)
    {
        var input = await inputs.LoadAsync(contract, last, null, ct, InvoiceType.Final);
        return Enum.GetValues<FinalPeriodCharge>().Select(charge =>
        {
            var (rent, services) = InvoiceCalculator.FinalPeriodAmounts(input, charge);
            return new FinalPeriodOptionDto(charge, rent, services);
        }).ToList();
    }

    /// <summary>Điều kiện cứng chặn hoàn tất (CT-BR-12) — tiền cần trả lại / khoản chưa xử lý / nợ là lựa chọn trên màn hoàn tất, không chặn.</summary>
    private static List<string> Blockers(
        Contract contract, DateOnly today, IReadOnlyList<Guid> missing, IReadOnlyList<InvoiceBalanceRow> invoices, InvoiceBalanceRow? final,
        bool finalRequired)
    {
        var blockers = new List<string>();
        if (contract.Status != ContractStatus.Liquidating)
            blockers.Add(ContractErrors.NotLiquidating.Code);
        else if (today < contract.ActualEndDate)
            blockers.Add(ContractErrors.LiquidationBeforeEndDate.Code);
        if (missing.Count > 0)
            blockers.Add(MeterErrors.FinalReadingRequired(missing).Code);
        if (invoices.Any(i => i.Status == InvoiceStatus.Draft))
            blockers.Add(BillingErrors.DraftExists.Code);
        if (finalRequired && final is not { Status: InvoiceStatus.Finalized })
            blockers.Add(BillingErrors.FinalRequired.Code);
        return blockers.Distinct().ToList();
    }
}
