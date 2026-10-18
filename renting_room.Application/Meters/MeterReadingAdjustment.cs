using FluentValidation;
using Mediator;
using Microsoft.EntityFrameworkCore;
using renting_room.Application.Billing;
using renting_room.Application.Common.Interfaces;
using renting_room.Application.Common.Validation;
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
public sealed class AdjustMeterReadingHandler(IAppDbContext db, TimeProvider clock) : IRequestHandler<AdjustMeterReadingCommand, Result<AdjustReadingResult>>
{
    public static readonly Warning LineManuallyEdited = new("LINE_MANUALLY_EDITED",
        "Dòng điện nước của phiếu đã chốt từng sửa tay — khoản chênh tính theo số hệ thống, kiểm tra và sửa khoản nếu cần.");

    public async ValueTask<Result<AdjustReadingResult>> Handle(AdjustMeterReadingCommand request, CancellationToken cancellationToken)
    {
        var reading = await db.Meters.SelectMany(m => m.Readings).Where(r => r.Id == request.ReadingId && r.VoidedAt == null)
            .Select(r => new { r.MeterId, r.Value }).FirstOrDefaultAsync(cancellationToken);
        if (reading is null)
            return MeterErrors.ReadingNotFound;
        var segment = await db.Invoices.Where(i => i.Status == InvoiceStatus.Finalized)
            .SelectMany(i => i.Segments.Where(s => s.EndReadingId == request.ReadingId && !s.Voided)
                .Select(s => new { InvoiceId = i.Id, i.ContractId, s.FeeTypeId, s.UsageStart }))
            .FirstOrDefaultAsync(cancellationToken);
        if (segment is null)
            return (await ReadingLocks.LockedReadingIdsAsync(db, [reading.MeterId], cancellationToken)).Contains(request.ReadingId)
                ? MeterErrors.ReadingNotAdjustable
                : MeterErrors.ReadingNotLocked;
        if (await db.Invoices.Where(i => i.Status == InvoiceStatus.Finalized)
                .AnyAsync(i => i.Segments.Any(s => s.StartReadingId == request.ReadingId && !s.Voided), cancellationToken))
            return MeterErrors.ReadingNextPeriodFinalized;
        if (request.Value == reading.Value)
            return MeterErrors.ReadingUnchanged;

        await using var transaction = await db.BeginTransactionAsync(cancellationToken);
        await db.LockForUpdateAsync<Contract>(segment.ContractId, cancellationToken);
        var contract = await db.Contracts.AsNoTracking().Where(c => c.Id == segment.ContractId)
            .Select(c => new { c.Id, c.PropertyId, c.RoomId, c.Status }).FirstAsync(cancellationToken);
        var finalClosed = await db.Invoices.AnyAsync(
            i => i.ContractId == contract.Id && i.Type == InvoiceType.Final && i.Status == InvoiceStatus.Finalized, cancellationToken);
        if (contract.Status is not (ContractStatus.Active or ContractStatus.Liquidating) || finalClosed)
            return RoomChargeErrors.NoOpenInvoice;

        var meter = (await MeterMapping.LockAndLoadAsync(db, [reading.MeterId], cancellationToken)).Single();
        var corrected = meter.Correct(request.ReadingId, request.Value, request.Reason);
        if (corrected.IsFailure)
            return corrected.Error!;

        var line = await db.Invoices.Where(i => i.Id == segment.InvoiceId).SelectMany(i => i.Lines)
            .Where(l => l.IsSystem && l.Type == InvoiceLineType.Metered && l.FeeTypeId == segment.FeeTypeId
                && (segment.UsageStart == null || l.ServiceFrom == segment.UsageStart))
            .FirstAsync(cancellationToken);
        var fee = await db.FeeTypes.AsNoTracking().Include(f => f.Prices).FirstAsync(f => f.Id == segment.FeeTypeId, cancellationToken);
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
            db.RoomCharges.Add(charge);
            if (await RoomChargeBilling.DraftForAsync(db, contract.Id, charge.IncurredOn, cancellationToken) is { } draft)
                RoomChargeBilling.Attach(draft, charge);
        }

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        var chargeDto = charge is null ? null : (await RoomChargeBilling.ToDtosAsync(db, [charge], cancellationToken))[0];
        return new AdjustReadingResult(corrected.Value!.ToDto(), chargeDto, warnings);
    }
}
