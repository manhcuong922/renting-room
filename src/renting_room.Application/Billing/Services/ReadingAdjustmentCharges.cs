using renting_room.Application.Common.Interfaces;
using renting_room.Domain.Billing;
using renting_room.Domain.Common;
using renting_room.Domain.Contracts;
using renting_room.Domain.Meters;

namespace renting_room.Application.Billing;

/// <summary>
/// M07 BL-BR-40: khoản điều chỉnh chỉ số — đúng 1 khoản chưa hủy cho mỗi dòng nguồn, gắn vào nháp nhận được. Gọi trong transaction của người
/// gọi, đã khóa HĐ và hóa đơn nguồn.
/// </summary>
public sealed class ReadingAdjustmentCharges(
    IRoomChargeRepository charges, IInvoiceRepository invoices, RoomChargeBilling roomCharges, IUnitOfWork unitOfWork, TimeProvider clock)
{
    /// <summary>
    /// Khoản hiện có của dòng nguồn. Khoản đã thanh toán — đã trả / thu ngay, hoặc đã nằm trên hóa đơn đã chốt — thì tiền đã đi rồi: sửa / hủy nó
    /// làm trả / thu 2 lần ⇒ <see cref="MeterErrors.AdjustmentAlreadyBilled"/>.
    /// </summary>
    public async Task<Result<RoomCharge?>> FindOpenAsync(Invoice source, InvoiceLine line, CancellationToken ct)
    {
        var charge = await charges.FindReadingAdjustmentForUpdateAsync(source.Id, line.FeeTypeId!.Value, line.ServiceFrom, ct);
        if (charge is { IsSettled: true })
            return MeterErrors.AdjustmentAlreadyBilled;
        if (charge?.InvoiceId is not { } holderId)
            return Result.Success(charge);
        await unitOfWork.LockForUpdateAsync<Invoice>(holderId, ct);
        var holder = await invoices.GetForUpdateAsync(holderId, ct);
        return holder is { Status: InvoiceStatus.Draft } ? Result.Success<RoomCharge?>(charge) : MeterErrors.AdjustmentAlreadyBilled;
    }

    public async Task<Result> CancelAsync(RoomCharge charge, string reason, CancellationToken ct)
    {
        var detached = await DetachAsync(charge, ct);
        return detached.IsFailure ? detached : charge.Cancel(reason, clock.GetUtcNow());
    }

    /// <summary>Đặt khoản của dòng nguồn = <paramref name="signedAmount"/> (dương: phụ thu, âm: bù, 0: hủy khoản).</summary>
    public async Task<Result<RoomCharge?>> SetAsync(
        Contract contract, Invoice source, InvoiceLine line, RoomCharge? existing, decimal signedAmount, string description, string reason,
        CancellationToken ct)
    {
        if (signedAmount == 0)
        {
            var cancelled = existing is null ? Result.Success() : await CancelAsync(existing, "Chỉ số đã về đúng số lúc chốt hóa đơn", ct);
            return cancelled.IsFailure ? cancelled.Error! : Result.Success<RoomCharge?>(null);
        }
        if (Math.Abs(signedAmount) > RoomCharge.MaxAmount)
            return MeterErrors.AdjustmentTooLarge;

        var kind = signedAmount > 0 ? RoomChargeKind.Surcharge : RoomChargeKind.Credit;
        var charge = existing;
        if (charge is null)
        {
            charge = RoomCharge.CreateReadingAdjustment(contract.PropertyId, contract.RoomId, contract.Id, kind, description, Math.Abs(signedAmount),
                clock.GetUtcNow().ToBusinessDate(), reason, source.Id, line.FeeTypeId!.Value, line.ServiceFrom);
            charges.Add(charge);
        }
        else
        {
            var detached = await DetachAsync(charge, ct);
            if (detached.IsFailure)
                return detached.Error!;
            charge.ReviseAdjustment(kind, Math.Abs(signedAmount), description, reason);
        }
        if (await roomCharges.DraftForAsync(contract.Id, charge.IncurredOn, ct) is { } draft)
            RoomChargeBilling.Attach(draft, charge);
        return charge;
    }

    private async Task<Result> DetachAsync(RoomCharge charge, CancellationToken ct)
    {
        if (charge.InvoiceId is not { } holderId || await invoices.GetForUpdateAsync(holderId, ct) is not { } holder)
            return Result.Success();
        var removed = holder.RemoveChargeLine(charge.Id);
        if (removed.IsSuccess)
            charge.Detach();
        return removed;
    }
}
