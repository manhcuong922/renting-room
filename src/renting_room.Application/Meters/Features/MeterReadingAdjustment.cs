using FluentValidation;
using Mediator;
using renting_room.Application.Billing;
using renting_room.Application.Common.Interfaces;
using renting_room.Application.Common.Validation;
using renting_room.Application.Contracts;
using renting_room.Application.Fees;
using renting_room.Application.Payments;
using renting_room.Domain.Billing;
using renting_room.Domain.Common;
using renting_room.Domain.Contracts;
using renting_room.Domain.Meters;
using renting_room.Domain.Payments;

namespace renting_room.Application.Meters;

public sealed record AdjustmentSettlement(DateOnly Date, PaymentMethod Method);

/// <param name="Charge">Khoản điều chỉnh khi "Đẩy sang hóa đơn sau"; null khi không có / đã hủy.</param>
/// <param name="Payments">Phiếu hoàn / phiếu thu thêm / phiếu bỏ nợ đảo và ghi lại trong lệnh này.</param>
public sealed record AdjustReadingResult(
    MeterReadingDto Reading, AdjustmentHandling Handling, InvoiceDetailDto Invoice, RoomChargeDto? Charge, IReadOnlyList<PaymentDto> Payments,
    IReadOnlyList<Warning> Warnings);

public sealed record AdjustMeterReadingCommand(
    Guid ReadingId, decimal Value, string Reason, AdjustmentHandling? Handling = null, AdjustmentSettlement? Settlement = null)
    : IRequest<Result<AdjustReadingResult>>;

public sealed class AdjustMeterReadingCommandValidator : AbstractValidator<AdjustMeterReadingCommand>
{
    public AdjustMeterReadingCommandValidator(TimeProvider clock)
    {
        RuleFor(x => x.Value).ReadingValue();
        RuleFor(x => x.Reason).RequiredText(300, "Lý do");
        RuleFor(x => x.Handling).Must(h => h is null or AdjustmentHandling.Immediate or AdjustmentHandling.NextInvoice)
            .WithErrorCode("INVALID_HANDLING").WithMessage("Chọn Immediate (Xử lý ngay) hoặc NextInvoice (Đẩy sang hóa đơn sau).");
        RuleFor(x => x.Settlement!.Date).Must(d => d <= clock.GetUtcNow().ToBusinessDate())
            .WithErrorCode("INVALID_SETTLED_DATE").WithMessage("Ngày trả / thu không được ở tương lai.").When(x => x.Settlement is not null);
        RuleFor(x => x.Settlement!.Method).IsInEnum().When(x => x.Settlement is not null);
    }
}

/// <summary>
/// MT-UC-09 / MT-BR-19: điều chỉnh chỉ số cuối của đoạn đo trên hóa đơn đã chốt. Khóa HĐ → công tơ → hóa đơn (C-07) rồi kiểm lại mọi điều kiện.
/// Hóa đơn chưa có tiền ⇒ tính lại trên hóa đơn; đã có tiền ⇒ Xử lý ngay (tính lại + hoàn / thu thêm) hoặc Đẩy sang hóa đơn sau (1 khoản khóa).
/// </summary>
public sealed class AdjustMeterReadingHandler(
    IMeterRepository meters, IMeterQueries meterQueries, IInvoiceRepository invoiceRepository, IInvoiceQueries invoices,
    IContractQueries contracts, IFeeQueries fees, ReadingAdjustmentCharges adjustmentCharges, RoomChargeBilling roomCharges,
    PaymentPosting posting, WriteOffReduction writeOffs, MeterDraftRefresher drafts, IUnitOfWork unitOfWork, TimeProvider clock)
    : IRequestHandler<AdjustMeterReadingCommand, Result<AdjustReadingResult>>
{
    public static readonly Warning LineManuallyEdited = new("LINE_MANUALLY_EDITED",
        "Dòng điện nước của hóa đơn đã chốt từng sửa tay — tiền chênh tính theo số hệ thống, kiểm tra lại.");

    public static readonly Warning ManualAmountClamped = new("MANUAL_AMOUNT_CLAMPED",
        "Dòng điện nước sửa tay cộng phần chênh ra âm — đã đặt 0đ, kiểm tra lại hóa đơn.");

    private sealed record Locked(Contract Contract, Meter Meter, Invoice Source, MeterReading Reading, IReadOnlyList<DraftRef> Drafts);

    private sealed record Applied(
        MeterReading Reading, AdjustmentHandling Handling, RoomCharge? Charge, List<Payment> Payments, IReadOnlyList<Warning> Warnings);

    private sealed record Outcome(RoomCharge? Charge, List<Payment> Payments, bool ManualAmountClamped = false);

    public async ValueTask<Result<AdjustReadingResult>> Handle(AdjustMeterReadingCommand request, CancellationToken cancellationToken)
    {
        var meterId = await meterQueries.GetMeterIdOfReadingAsync(request.ReadingId, cancellationToken);
        if (meterId is null)
            return MeterErrors.ReadingNotFound;
        var segment = await invoices.FindFinalizedSegmentEndingAtAsync(request.ReadingId, cancellationToken);
        if (segment is null)
            return (await invoices.GetLockedReadingIdsAsync([meterId.Value], cancellationToken)).Contains(request.ReadingId)
                ? MeterErrors.ReadingNotAdjustable
                : MeterErrors.ReadingNotLocked;

        await using var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken);
        var locked = await LockAsync(request, meterId.Value, segment, cancellationToken);
        if (locked.IsFailure)
            return locked.Error!;
        var applied = await ApplyAsync(request, locked.Value!, cancellationToken);
        if (applied.IsFailure)
            return applied.Error!;
        await drafts.RecalculateAsync(locked.Value!.Drafts, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return await ToResultAsync(locked.Value!.Source, applied.Value!, cancellationToken);
    }

    private async Task<Result<Locked>> LockAsync(AdjustMeterReadingCommand request, Guid meterId, SegmentRef segment, CancellationToken ct)
    {
        // Nháp kỳ sau bắt đầu từ chỉ số này ⇒ tính lại cùng lệnh (MT-BR-06): khóa HĐ nguồn và HĐ của nháp một lượt.
        var affectedDrafts = await drafts.LockContractsAsync([meterId], ct, segment.ContractId);
        var contract = (await contracts.GetAsync(segment.ContractId, ct))!;
        if (contract.Status == ContractStatus.Ended)
            return MeterErrors.AdjustmentContractEnded;
        if (contract.Status is not (ContractStatus.Active or ContractStatus.Liquidating))
            return RoomChargeErrors.NoOpenInvoice;

        var meter = (await meters.LockAndLoadAsync([meterId], ct)).Single();
        await unitOfWork.LockForUpdateAsync<Invoice>(segment.InvoiceId, ct);
        var source = (await invoiceRepository.GetForUpdateAsync(segment.InvoiceId, ct))!;
        if (meter.Find(request.ReadingId) is not { } reading)
            return MeterErrors.ReadingNotFound;
        if (source.Status != InvoiceStatus.Finalized || source.SegmentEndingAt(reading.Id) is null)
            return MeterErrors.ReadingNotLocked;
        if (await invoices.HasFinalizedSegmentStartingAtAsync(reading.Id, ct))
            return MeterErrors.ReadingNextPeriodFinalized;
        if (request.Value == reading.Value)
            return MeterErrors.ReadingUnchanged;
        return new Locked(contract, meter, source, reading, affectedDrafts);
    }

    /// <summary>Sửa chỉ số (kiểm đơn điệu trước), tính tiền chênh, xử lý theo cách đã chọn, ghi lịch sử — chưa lưu.</summary>
    private async Task<Result<Applied>> ApplyAsync(AdjustMeterReadingCommand request, Locked locked, CancellationToken ct)
    {
        var (contract, meter, source, reading, _) = locked;
        var oldValue = reading.Value;
        var line = source.LineOf(source.SegmentEndingAt(reading.Id)!)!;
        var corrected = meter.Correct(reading.Id, request.Value, request.Reason);
        if (corrected.IsFailure)
            return corrected.Error!;
        var fallbackPrice = await FallbackPriceAsync(line, ct);
        var change = source.PreviewReadingChange(reading.Id, request.Value, fallbackPrice);
        if (change.IsFailure)
            return change.Error!;

        var handling = await ChooseHandlingAsync(request, contract, source, ct);
        if (handling.IsFailure)
            return handling.Error!;
        var existing = await adjustmentCharges.FindOpenAsync(source, line, ct);
        if (existing.IsFailure)
            return existing.Error!;
        var outcome = handling.Value == AdjustmentHandling.NextInvoice
            ? await PushToNextInvoiceAsync(request, contract, source, line, existing.Value, change.Value!, ct)
            : await RecalculateSourceAsync(request, contract, source, existing.Value, handling.Value, fallbackPrice, ct);
        if (outcome.IsFailure)
            return outcome.Error!;

        var (charge, payments, clamped) = outcome.Value!;
        invoiceRepository.AddAdjustment(InvoiceAdjustment.Record(source, reading.Id, change.Value!, oldValue, request.Value, handling.Value,
            charge?.Id, payments.Select(p => p.Id).ToList(), request.Reason));
        var warnings = new List<Warning>();
        if (line.IsManuallyEdited)
            warnings.Add(LineManuallyEdited);
        if (clamped)
            warnings.Add(ManualAmountClamped);
        return new Applied(corrected.Value!, handling.Value, charge, payments, warnings);
    }

    private async Task<AdjustReadingResult> ToResultAsync(Invoice source, Applied applied, CancellationToken ct) =>
        new(applied.Reading.ToDto(), applied.Handling, source.ToDetail(clock.GetUtcNow().ToBusinessDate()),
            applied.Charge is null ? null : (await roomCharges.ToDtosAsync([applied.Charge], ct))[0],
            await PaymentMapping.ToDtosAsync(invoices, applied.Payments, ct), applied.Warnings);

    /// <summary>
    /// Đã xác nhận hoàn (hóa đơn âm) ⇒ phải bỏ xác nhận trước; chưa có tiền ⇒ tính lại trên hóa đơn; đã có tiền ⇒ bắt buộc chọn; hóa đơn trả
    /// phòng đã chốt ⇒ không còn hóa đơn sau.
    /// </summary>
    private async Task<Result<AdjustmentHandling>> ChooseHandlingAsync(
        AdjustMeterReadingCommand request, Contract contract, Invoice source, CancellationToken ct)
    {
        if (source.RefundedOn is not null)
            return BillingErrors.RefundConfirmed;
        if (source.PaidAmount == 0)
            return AdjustmentHandling.Recalculated;
        if (request.Handling is not { } chosen)
            return MeterErrors.AdjustmentHandlingRequired;
        if (chosen == AdjustmentHandling.NextInvoice && (source.Type == InvoiceType.Final || await invoices.HasFinalizedFinalAsync(contract.Id, ct)))
            return MeterErrors.AdjustmentNoNextInvoice;
        return chosen;
    }

    private async Task<Result<Outcome>> RecalculateSourceAsync(
        AdjustMeterReadingCommand request, Contract contract, Invoice source, RoomCharge? existing, AdjustmentHandling handling,
        LinePrice? fallbackPrice, CancellationToken ct)
    {
        if (existing is not null)
        {
            var cancelled = await adjustmentCharges.CancelAsync(existing, "Điều chỉnh lại bằng cách tính lại trên hóa đơn nguồn", ct);
            if (cancelled.IsFailure)
                return cancelled.Error!;
        }
        var applied = source.ApplyReadingChange(request.ReadingId, request.Value, fallbackPrice);
        if (applied.IsFailure)
            return applied.Error!;
        var clamped = applied.Value!.ManualAmountClamped;
        if (handling == AdjustmentHandling.Recalculated)
            return new Outcome(null, [], clamped);
        var settled = await SettleAsync(request, contract, source, ct);
        return settled.IsFailure ? settled.Error! : new Outcome(null, settled.Value!, clamped);
    }

    /// <summary>Thu dư: hoàn phần trả thừa thật trước, rồi giảm phần bỏ nợ; thu thiếu + có <c>settlement</c>: ghi phiếu thu phần thiếu.</summary>
    private async Task<Result<List<Payment>>> SettleAsync(AdjustMeterReadingCommand request, Contract contract, Invoice source, CancellationToken ct)
    {
        var date = request.Settlement?.Date ?? clock.GetUtcNow().ToBusinessDate();
        var method = request.Settlement?.Method ?? PaymentMethod.Cash;
        var payments = new List<Payment>();
        var excess = source.PaidAmount - Math.Max(source.TotalAmount, 0);
        if (excess > 0)
        {
            var reduce = Math.Min(excess, source.WrittenOffAmount);
            if (excess - reduce > 0)
                payments.Add(await posting.PostRefundAsync(contract, source, excess - reduce, date, method,
                    $"Hoàn tiền sau điều chỉnh chỉ số — {request.Reason.Trim()}", ct));
            if (reduce > 0)
                payments.AddRange(await writeOffs.ReduceAsync(contract, source, reduce, "Điều chỉnh chỉ số — giảm phần bỏ nợ", clock.GetUtcNow(), ct));
            return payments;
        }
        if (source.Outstanding > 0 && request.Settlement is not null)
        {
            // Ghi phiếu thu tìm hóa đơn còn nợ trong DB ⇒ lưu tổng mới trước.
            await unitOfWork.SaveChangesAsync(ct);
            var receipt = await posting.PostAsync(contract, source.Outstanding, method, date, source.Id, null, null,
                $"Thu thêm sau điều chỉnh chỉ số — {request.Reason.Trim()}", PaymentKind.Receipt, ct);
            if (receipt.IsFailure)
                return receipt.Error!;
            payments.Add(receipt.Value!);
        }
        return payments;
    }

    /// <summary>Hóa đơn giữ nguyên tổng; khoản = chênh so với số đã tính lúc chốt — thu dư trừ phần đã bỏ nợ của hóa đơn.</summary>
    private async Task<Result<Outcome>> PushToNextInvoiceAsync(
        AdjustMeterReadingCommand request, Contract contract, Invoice source, InvoiceLine line, RoomCharge? existing, MeteredLineChange change,
        CancellationToken ct)
    {
        var difference = change.Difference;
        if (difference < 0)
            difference = Math.Min(difference + source.WrittenOffAmount, 0);
        var fee = (await fees.FindTypeAsync(line.FeeTypeId!.Value, ct))!;
        var billedEnd = source.SegmentEndingAt(request.ReadingId)!.EndValue;
        var delta = request.Value - billedEnd;
        var description = $"Điều chỉnh chỉ số {fee.Name} kỳ {line.ServiceFrom:dd/MM}–{line.ServiceTo:dd/MM}: " +
            $"{billedEnd:0.##} → {request.Value:0.##} ({(delta < 0 ? "−" : "+")}{Math.Abs(delta):0.##} {fee.Unit})";
        var charge = await adjustmentCharges.SetAsync(contract, source, line, existing, difference, description, request.Reason, ct);
        if (charge.IsFailure)
            return charge.Error!;
        source.MarkAdjusted();
        return new Outcome(charge.Value, []);
    }

    private async Task<LinePrice?> FallbackPriceAsync(InvoiceLine line, CancellationToken ct)
    {
        if (line.Price is not null)
            return null;
        var fee = await fees.GetWithPricesAsync(line.FeeTypeId!.Value, ct);
        return fee?.ResolvePrice(line.ServiceTo) is { } price ? LinePrice.Of(price, line.SystemQuantity ?? line.Quantity) : null;
    }
}
