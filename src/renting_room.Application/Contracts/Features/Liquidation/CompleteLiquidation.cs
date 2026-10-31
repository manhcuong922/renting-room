using FluentValidation;
using Mediator;
using renting_room.Application.Billing;
using renting_room.Application.Common.Interfaces;
using renting_room.Application.Common.Validation;
using renting_room.Application.Identity;
using renting_room.Application.Meters;
using renting_room.Application.Payments;
using renting_room.Application.Properties;
using renting_room.Domain.Billing;
using renting_room.Domain.Common;
using renting_room.Domain.Contracts;
using renting_room.Domain.Identity;
using renting_room.Domain.Meters;
using renting_room.Domain.Payments;

namespace renting_room.Application.Contracts;

/// <summary>
/// "Đã trả" — xác nhận đã hoàn mọi hóa đơn Chờ hoàn của HĐ (M07 BL-BR-27), hoặc "Không hoàn" (<paramref name="Waive"/>, vd người thuê bỏ đi):
/// chỉ chủ trọ, bắt buộc <paramref name="Reason"/>, không có <paramref name="Method"/>.
/// </summary>
public sealed record LiquidationRefund(DateOnly RefundedOn, PaymentMethod? Method = null, bool Waive = false, string? Reason = null);

/// <summary>Khoản bù / phụ thu chưa xử lý coi như đã trả / thu tay vào ngày hoàn tất (BL-BR-31).</summary>
public sealed record LiquidationPendingCharges(PaymentMethod Method);

public enum DepositAction
{
    /// <summary>Trả cọc — số tiền sửa được, trả thiếu ⇒ lý do (PM-BR-33).</summary>
    Refund,

    /// <summary>Không trả cọc — lý do bắt buộc.</summary>
    Forfeit,

    /// <summary>Để sau — cọc vẫn "Đang giữ".</summary>
    Later
}

public sealed record LiquidationDeposit(DepositAction Action, decimal? Amount, DateOnly? Date, string? Note);

/// <summary>Kỳ trả phòng đã xử lý ngoài hệ thống (M07 BL-BR-46): không lập hóa đơn trả phòng, ghi khoảng ngắt tính tiền.</summary>
public sealed record LiquidationExternalSettlement(string? Note);

/// <param name="Settlement">Bắt buộc khi còn nợ; null mà còn nợ ⇒ 422 CONTRACT_HAS_DEBT (cảnh báo kèm số nợ).</param>
/// <param name="Method">Phương thức khi "Đã thu toàn bộ" — mặc định tiền mặt.</param>
/// <param name="Reason">Lý do bỏ nợ (bắt buộc khi WriteOff) / ghi chú phiếu thu.</param>
/// <param name="Refund">Bắt buộc khi còn tiền cần trả lại người thuê (REFUND_PENDING).</param>
/// <param name="PendingCharges">Bắt buộc khi còn khoản chưa xử lý (PENDING_CHARGES).</param>
public sealed record CompleteLiquidationCommand(
    Guid ContractId, DebtSettlement? Settlement = null, PaymentMethod? Method = null, DateOnly? PaidAt = null, string? Reason = null,
    LiquidationRefund? Refund = null, LiquidationPendingCharges? PendingCharges = null, LiquidationDeposit? Deposit = null,
    LiquidationExternalSettlement? ExternalSettlement = null, IReadOnlyList<MeterReadingInput>? FinalReadings = null)
    : IRequest<Result>;

public sealed class CompleteLiquidationCommandValidator : AbstractValidator<CompleteLiquidationCommand>
{
    public CompleteLiquidationCommandValidator(TimeProvider clock)
    {
        RuleFor(x => x.Settlement).IsInEnum().When(x => x.Settlement is not null);
        RuleFor(x => x.Method).IsInEnum().When(x => x.Method is not null);
        RuleFor(x => x.Reason).OptionalText(500);
        RuleFor(x => x.Reason).NotEmpty().When(x => x.Settlement == DebtSettlement.WriteOff)
            .WithErrorCode("REQUIRED").WithMessage("Nhập lý do bỏ nợ.");
        RuleFor(x => x.PaidAt).Must(d => d is null || d <= clock.GetUtcNow().ToBusinessDate())
            .WithErrorCode("INVALID_PAID_AT").WithMessage("Ngày thu không được ở tương lai.");
        RuleFor(x => x.Refund!.Method).IsInEnum().When(x => x.Refund?.Method is not null);
        RuleFor(x => x.Refund!.Method).NotNull().When(x => x.Refund is { Waive: false })
            .WithErrorCode("REQUIRED").WithMessage("Chọn cách đã hoàn tiền.");
        RuleFor(x => x.Refund!.Reason).NotEmpty().When(x => x.Refund is { Waive: true })
            .WithErrorCode("REQUIRED").WithMessage("Nhập lý do không hoàn tiền.");
        RuleFor(x => x.Refund!.Reason).OptionalText(300).When(x => x.Refund is not null);
        RuleFor(x => x.PendingCharges!.Method).IsInEnum().When(x => x.PendingCharges is not null);
        RuleFor(x => x.Deposit!.Action).IsInEnum().When(x => x.Deposit is not null);
        RuleFor(x => x.Deposit!.Amount).OptionalMoney().When(x => x.Deposit is not null);
        RuleFor(x => x.Deposit!.Note).OptionalText(500).When(x => x.Deposit is not null);
        RuleFor(x => x.ExternalSettlement!.Note).OptionalText(500).When(x => x.ExternalSettlement is not null);
    }
}

/// <summary>M05 CT-BR-12: hoàn tất thanh lý một lần bấm — trả lại → khoản chưa xử lý → nợ → cọc, trong 1 transaction đã khóa HĐ (C-07).</summary>
public sealed class CompleteLiquidationHandler(
    ContractMutation mutation, ContractMeterReadings meterReadings, IInvoiceQueries invoiceQueries, IInvoiceRepository invoices,
    IRoomChargeRepository roomCharges, IPropertyQueries properties, IUserQueries users, PaymentPosting posting, ICurrentUser currentUser,
    LiquidationDrafts drafts, BillingGapPlanner gapPlanner, IBillingGapRepository gapRepository, TimeProvider clock)
    : IRequestHandler<CompleteLiquidationCommand, Result>
{
    private const string RefundNote = "Xác nhận khi hoàn tất thanh lý";

    public ValueTask<Result> Handle(CompleteLiquidationCommand request, CancellationToken cancellationToken) =>
        new(mutation.RunAsync(request.ContractId, async contract =>
        {
            var now = clock.GetUtcNow();
            var ready = request.ExternalSettlement is { } external
                ? await SettleExternallyAsync(contract, external, request.FinalReadings, now.ToBusinessDate(), cancellationToken)
                : await CheckReadyAsync(contract, request.FinalReadings, now.ToBusinessDate(), cancellationToken);
            if (ready.IsFailure)
                return ready;
            var steps = await ConfirmRefundsAsync(contract, request.Refund, now.ToBusinessDate(), cancellationToken);
            if (steps.IsSuccess)
                steps = await SettlePendingChargesAsync(contract, request.PendingCharges, now.ToBusinessDate(), cancellationToken);
            if (steps.IsSuccess)
                steps = await SettleDebtAsync(contract, request, now.ToBusinessDate(), cancellationToken);
            if (steps.IsSuccess)
                steps = DecideDeposit(contract, request.Deposit, now.ToBusinessDate());
            return steps.IsFailure ? steps : contract.CompleteLiquidation(now);
        }, cancellationToken));

    /// <summary>Chỉ số cuối bắt buộc trong mọi trường hợp (H-02) — ngày trả phòng tương lai thì nhập ở đây / hóa đơn trả phòng.</summary>
    private async Task<Result> EnsureFinalReadingsAsync(Contract contract, IReadOnlyList<MeterReadingInput>? readings, CancellationToken ct) =>
        ContractMeterReadings.FinalRequiredAt(contract, contract.ActualEndDate!.Value)
            ? await meterReadings.RecordAsync(contract, ReadingKind.Final, contract.ActualEndDate!.Value, readings, ct)
            : Result.Success();

    private async Task<Result> CheckReadyAsync(Contract contract, IReadOnlyList<MeterReadingInput>? readings, DateOnly today, CancellationToken ct)
    {
        if (contract.Status != ContractStatus.Liquidating)
            return Result.Failure(ContractErrors.NotLiquidating);
        if (today < contract.ActualEndDate)
            return Result.Failure(ContractErrors.LiquidationBeforeEndDate);
        if (await invoiceQueries.HasDraftAsync(contract.Id, ct))
            return Result.Failure(BillingErrors.DraftExists);
        var finals = await EnsureFinalReadingsAsync(contract, readings, ct);
        if (finals.IsFailure)
            return finals;
        // Không còn phần chưa thu (CT-UC-08) ⇒ không cần hóa đơn trả phòng — số chốt kỳ là số cuối của kỳ cuối.
        var schedule = (await properties.GetBillingAsync(contract.PropertyId, ct)).Schedule;
        if (!FinalInvoiceNeed.IsRequired(schedule, contract.BillingStartDate, contract.ActualEndDate!.Value,
                await invoiceQueries.ListPeriodsAsync(contract.Id, ct)))
            return Result.Success();
        return await invoiceQueries.HasFinalizedFinalAsync(contract.Id, ct) ? Result.Success() : Result.Failure(BillingErrors.FinalRequired);
    }

    /// <summary>
    /// M07 BL-BR-46: "đã xử lý ngoài hệ thống" — xóa nháp sót, ghi khoảng ngắt phủ mọi kỳ chưa có phiếu đến hết kỳ cuối, không cần hóa đơn trả phòng
    /// (chỉ số cuối vẫn bắt buộc như mọi lần thanh lý). Nợ, cọc, khoản chờ vẫn xử lý ở các bước sau.
    /// </summary>
    private async Task<Result> SettleExternallyAsync(
        Contract contract, LiquidationExternalSettlement external, IReadOnlyList<MeterReadingInput>? readings, DateOnly today, CancellationToken ct)
    {
        if (contract.Status != ContractStatus.Liquidating)
            return Result.Failure(ContractErrors.NotLiquidating);
        if (today < contract.ActualEndDate)
            return Result.Failure(ContractErrors.LiquidationBeforeEndDate);
        if (string.IsNullOrWhiteSpace(external.Note))
            return Result.Failure(BillingErrors.ExternalNoteRequired);
        var finals = await EnsureFinalReadingsAsync(contract, readings, ct);
        if (finals.IsFailure)
            return finals;

        var endDate = contract.ActualEndDate!.Value;
        var schedule = (await properties.GetBillingAsync(contract.PropertyId, ct)).Schedule;
        var last = contract.BillingPeriods(schedule, endDate).LastOrDefault();
        var beforeDrafts = await invoiceQueries.ListPeriodsAsync(contract.Id, ct);
        if (last is null || beforeDrafts.Any(i => i.Status != InvoiceStatus.Draft && i.PeriodStart <= last.End && i.PeriodEnd >= last.Start))
            return Result.Failure(BillingErrors.ExternalSettlementNotAllowed);

        await drafts.DeleteAllAsync(contract.Id, ct);
        var invoiceRows = (await invoiceQueries.ListPeriodsAsync(contract.Id, ct)).Select(i => (i.PeriodStart, i.PeriodEnd, i.Status)).ToList();
        // Kỳ giả ngay sau ngày trả phòng: mọi kỳ của HĐ (kể cả kỳ cuối) đều "đứng trước" nó.
        var after = new BillingPeriod(endDate.AddDays(1), endDate.AddDays(1), new DateOnly(endDate.Year, endDate.Month, 1));
        var plan = await gapPlanner.PlanAsync(contract, schedule, after, invoiceRows, true, ct);
        if (plan.Error is { } error)
            return Result.Failure(error);
        if (!plan.Skip || plan.Unbilled[^1].End != last.End)
            return Result.Failure(BillingErrors.ExternalSettlementNotAllowed);

        gapRepository.Add(ContractBillingGap.Create(contract.Id, plan.Unbilled, null, external.Note));
        return Result.Success();
    }

    private async Task<Result> ConfirmRefundsAsync(Contract contract, LiquidationRefund? refund, DateOnly today, CancellationToken ct)
    {
        var ids = await invoiceQueries.ListRefundPendingIdsAsync(contract.Id, ct);
        if (ids.Count == 0)
            return Result.Success();
        if (refund is null)
            return Result.Failure(BillingErrors.RefundPending(await invoiceQueries.GetRefundDueAsync(contract.Id, ct)));
        if (refund.Waive && currentUser.Role != UserRole.OrgOwner)
            return Result.Failure(BillingErrors.RefundWaiveNotAllowed);
        foreach (var invoice in (await invoices.LockForUpdateAsync(ids, ct)).Values)
        {
            var confirmed = refund.Waive
                ? invoice.WaiveRefund(refund.RefundedOn, refund.Reason, today)
                : invoice.ConfirmRefund(refund.RefundedOn, refund.Method!.Value, RefundNote, today);
            if (confirmed.IsFailure)
                return confirmed;
        }
        return Result.Success();
    }

    private async Task<Result> SettlePendingChargesAsync(Contract contract, LiquidationPendingCharges? decision, DateOnly today, CancellationToken ct)
    {
        var pending = (await roomCharges.ListPendingByContractAsync(contract.Id, ct)).Where(c => !c.IsSettled).ToList();
        if (pending.Count == 0)
            return Result.Success();
        if (decision is null)
            return Result.Failure(BillingErrors.PendingCharges(pending.Count,
                pending.Where(c => c.Kind == RoomChargeKind.Surcharge).Sum(c => c.Amount),
                pending.Where(c => c.Kind == RoomChargeKind.Credit).Sum(c => c.Amount)));
        foreach (var charge in pending)
        {
            var settled = charge.Settle(today, decision.Method, today);
            if (settled.IsFailure)
                return settled;
        }
        return Result.Success();
    }

    private async Task<Result> SettleDebtAsync(Contract contract, CompleteLiquidationCommand request, DateOnly today, CancellationToken ct)
    {
        var outstanding = await invoiceQueries.GetContractOutstandingAsync(contract.Id, ct);
        if (outstanding <= 0)
            return Result.Success();
        if (request.Settlement is null)
            return Result.Failure(BillingErrors.HasDebt(outstanding));
        var writeOff = request.Settlement == DebtSettlement.WriteOff;
        if (writeOff && !await WriteOffGuard.IsAllowedAsync(users, currentUser, ct))
            return Result.Failure(WriteOffErrors.NotAllowed);
        var posted = await posting.PostAsync(contract, null, request.Method ?? PaymentMethod.Cash, request.PaidAt ?? today, null, null, null,
            writeOff ? request.Reason : request.Reason ?? "Thu toàn bộ khi trả phòng", writeOff ? PaymentKind.WriteOff : PaymentKind.Receipt, ct);
        return posted.IsFailure ? Result.Failure(posted.Error!) : Result.Success();
    }

    private static Result DecideDeposit(Contract contract, LiquidationDeposit? deposit, DateOnly today) => deposit?.Action switch
    {
        DepositAction.Refund => contract.RefundDeposit(deposit.Date ?? today, deposit.Amount, deposit.Note, today),
        DepositAction.Forfeit => contract.ForfeitDeposit(deposit.Date ?? today, deposit.Note, today),
        _ => Result.Success()
    };
}
