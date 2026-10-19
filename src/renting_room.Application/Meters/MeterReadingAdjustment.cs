using FluentValidation;
using Mediator;
using renting_room.Application.Billing;
using renting_room.Application.Common.Interfaces;
using renting_room.Application.Common.Validation;
using renting_room.Application.Contracts;
using renting_room.Application.Fees;
using renting_room.Domain.Billing;
using renting_room.Domain.Common;
using renting_room.Domain.Contracts;
using renting_room.Domain.Meters;

namespace renting_room.Application.Meters;

/// <param name="Charge">Khoản bù / phụ thu tiền chênh; null khi chênh 0đ.</param>
public sealed record AdjustReadingResult(MeterReadingDto Reading, RoomChargeDto? Charge, IReadOnlyList<Warning> Warnings);

public sealed record AdjustMeterReadingCommand(Guid ReadingId, decimal Value, string Reason) : IRequest<Result<AdjustReadingResult>>;

public sealed class AdjustMeterReadingCommandValidator : AbstractValidator<AdjustMeterReadingCommand>
{
    public AdjustMeterReadingCommandValidator()
    {
        RuleFor(x => x.Value).ReadingValue();
        RuleFor(x => x.Reason).RequiredText(300, "Lý do");
    }
}

/// <summary>
/// MT-UC-09 / MT-BR-19: điều chỉnh chỉ số cuối của đoạn đo trên phiếu đã chốt (gõ sai, phát hiện sau khi chốt / thu) — sửa chỉ số về số đúng và
/// tạo khoản bù (thu dư) / phụ thu (thu thiếu) phần tiền chênh của dòng điện nước, vào phiếu kỳ sau. Phiếu đã chốt giữ nguyên làm chứng từ.
/// 1 transaction, khóa HĐ → công tơ → nháp (C-07).
/// </summary>
public sealed class AdjustMeterReadingHandler(
    IMeterRepository meters, IMeterQueries meterQueries, IInvoiceQueries invoices, IContractQueries contracts, IFeeQueries fees,
    IRoomChargeRepository roomChargeRepository, RoomChargeBilling roomCharges, IUnitOfWork unitOfWork, TimeProvider clock)
    : IRequestHandler<AdjustMeterReadingCommand, Result<AdjustReadingResult>>
{
    public static readonly Warning LineManuallyEdited = new("LINE_MANUALLY_EDITED",
        "Dòng điện nước của phiếu đã chốt từng sửa tay — khoản chênh tính theo số hệ thống, kiểm tra và sửa khoản nếu cần.");

    public async ValueTask<Result<AdjustReadingResult>> Handle(AdjustMeterReadingCommand request, CancellationToken cancellationToken)
    {
        var reading = await meterQueries.GetActiveReadingAsync(request.ReadingId, cancellationToken);
        if (reading is null)
            return MeterErrors.ReadingNotFound;
        var segment = await invoices.FindFinalizedSegmentEndingAtAsync(request.ReadingId, cancellationToken);
        if (segment is null)
            return (await invoices.GetLockedReadingIdsAsync([reading.MeterId], cancellationToken)).Contains(request.ReadingId)
                ? MeterErrors.ReadingNotAdjustable
                : MeterErrors.ReadingNotLocked;
        if (await invoices.HasFinalizedSegmentStartingAtAsync(request.ReadingId, cancellationToken))
            return MeterErrors.ReadingNextPeriodFinalized;
        if (request.Value == reading.Value)
            return MeterErrors.ReadingUnchanged;

        await using var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken);
        await unitOfWork.LockForUpdateAsync<Contract>(segment.ContractId, cancellationToken);
        var contract = (await contracts.GetAsync(segment.ContractId, cancellationToken))!;
        var finalClosed = await invoices.HasFinalizedFinalAsync(contract.Id, cancellationToken);
        if (contract.Status is not (ContractStatus.Active or ContractStatus.Liquidating) || finalClosed)
            return RoomChargeErrors.NoOpenInvoice;

        var meter = (await meters.LockAndLoadAsync([reading.MeterId], cancellationToken)).Single();
        var corrected = meter.Correct(request.ReadingId, request.Value, request.Reason);
        if (corrected.IsFailure)
            return corrected.Error!;

        var line = await invoices.GetSystemMeteredLineAsync(segment.InvoiceId, segment.FeeTypeId, segment.UsageStart, cancellationToken);
        var fee = (await fees.GetWithPricesAsync(segment.FeeTypeId, cancellationToken))!;
        if (fee.ResolvePrice(line.ServiceTo) is not { } price)
            return Error.BusinessRule("FEE_PRICE_MISSING", $"\"{fee.Name}\" chưa có giá cho kỳ của phiếu.");

        // Tiền chênh = dòng tính lại với sản lượng mới − với sản lượng cũ, cùng bản giá của dòng (theo bậc ⇒ bậc trên tổng của dòng).
        var delta = reading.Value - request.Value;
        var quantity = line.IsManuallyEdited ? line.SystemQuantity ?? line.Quantity : line.Quantity;
        var difference = Invoice.Money(price.Amount(quantity)) - Invoice.Money(price.Amount(quantity - delta));
        var warnings = line.IsManuallyEdited ? new List<Warning> { LineManuallyEdited } : [];
        RoomCharge? charge = null;
        if (difference != 0)
        {
            if (Math.Abs(difference) > RoomCharge.MaxAmount)
                return MeterErrors.AdjustmentTooLarge;
            var description = $"Điều chỉnh chỉ số {fee.Name} kỳ {line.ServiceFrom:dd/MM}–{line.ServiceTo:dd/MM}: " +
                $"{reading.Value:0.##} → {request.Value:0.##} ({(delta > 0 ? "−" : "+")}{Math.Abs(delta):0.##} {fee.Unit})";
            charge = RoomCharge.Create(contract.PropertyId, contract.RoomId, contract.Id,
                difference > 0 ? RoomChargeKind.Credit : RoomChargeKind.Surcharge, description, Math.Abs(difference),
                clock.GetUtcNow().ToBusinessDate(), request.Reason);
            roomChargeRepository.Add(charge);
            if (await roomCharges.DraftForAsync(contract.Id, charge.IncurredOn, cancellationToken) is { } draft)
                RoomChargeBilling.Attach(draft, charge);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        var chargeDto = charge is null ? null : (await roomCharges.ToDtosAsync([charge], cancellationToken))[0];
        return new AdjustReadingResult(corrected.Value!.ToDto(), chargeDto, warnings);
    }
}
