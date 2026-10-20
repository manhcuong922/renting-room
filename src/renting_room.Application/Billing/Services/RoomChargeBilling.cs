using FluentValidation;
using renting_room.Application.Common.Interfaces;
using renting_room.Application.Rooms;
using renting_room.Domain.Billing;
using renting_room.Domain.Common;
using renting_room.Domain.Contracts;

namespace renting_room.Application.Billing;

public enum RoomChargeStatus
{
    Pending,

    OnDraft,

    /// <summary>Đã vào phiếu đã chốt — khóa.</summary>
    Billed,
    Cancelled
}

public sealed record RoomChargeDto(
    Guid Id, Guid RoomId, string RoomCode, Guid ContractId, RoomChargeKind Kind, string Description, decimal Amount, DateOnly IncurredOn,
    string Reason, bool IsSettled, DateOnly? SettledOn, PaymentMethod? SettledMethod, RoomChargeStatus Status, Guid? InvoiceId,
    string? InvoiceNo, string? BillingMonth, string? CancelReason, DateTimeOffset CreatedAt, bool IsSystem = false, Guid? FeeTypeId = null,
    decimal? Quantity = null, decimal? UnitPrice = null);

public sealed record ChargeSettlement(DateOnly SettledOn, PaymentMethod Method);

/// <summary>
/// M07 BL-BR-32..36 — gắn / gỡ khoản phát sinh với phiếu, trong transaction của người gọi (đã khóa HĐ / phiếu).
/// </summary>
public sealed class RoomChargeBilling(
    IRoomChargeRepository charges, IRoomChargeQueries chargeQueries, IInvoiceRepository invoices, IInvoiceQueries invoiceQueries,
    IRoomQueries rooms, IUnitOfWork unitOfWork)
{
    public const string NotAttachedCode = "ROOM_CHARGE_NOT_ATTACHED";

    public async Task AttachPendingAsync(Invoice draft, CancellationToken ct)
    {
        var pending = await charges.ListPendingByContractAsync(draft.ContractId, ct);
        foreach (var charge in pending.Where(c => draft.Type == InvoiceType.Final || c.IncurredOn <= draft.PeriodEnd))
            Attach(draft, charge);
    }

    public static Result Attach(Invoice draft, RoomCharge charge)
    {
        var added = draft.AddManualLine(charge.LineType, charge.Description, charge.Quantity, charge.UnitPrice, charge.Amount, charge.Reason,
            charge.FeeTypeId, roomChargeId: charge.Id, isSettled: charge.IsSettled);
        if (added.IsSuccess)
        {
            charge.AttachTo(draft.Id);
            return Result.Success();
        }
        if (added.Error == BillingErrors.NegativeTotal)
            draft.AddIssue(new InvoiceIssue(NotAttachedCode, InvoiceIssue.Warning,
                $"Khoản bù \"{charge.Description}\" ({charge.Amount:N0}đ) lớn hơn phần thu của phiếu — chưa đưa vào, sẽ chờ phiếu sau.", charge.Id));
        return Result.Failure(added.Error!);
    }

    public Result AddManualLine(
        Invoice invoice, InvoiceLineType type, string description, decimal? quantity, decimal? unitPrice, decimal amount,
        string note, Guid? feeTypeId, Guid? sourceInvoiceId, ChargeSettlement? settlement, DateOnly today)
    {
        if (type == InvoiceLineType.Refund)
        {
            var refund = invoice.AddManualLine(type, description, quantity, unitPrice, amount, note, feeTypeId, sourceInvoiceId);
            return refund.IsSuccess ? Result.Success() : Result.Failure(refund.Error!);
        }

        var kind = type == InvoiceLineType.Surcharge ? RoomChargeKind.Surcharge : RoomChargeKind.Credit;
        var incurredOn = invoice.PeriodEnd < today ? invoice.PeriodEnd : today;
        var charge = RoomCharge.Create(invoice.PropertyId, invoice.RoomId, invoice.ContractId, kind, description, amount, incurredOn, note);
        if (settlement is not null)
        {
            var settled = charge.Settle(settlement.SettledOn, settlement.Method, today);
            if (settled.IsFailure)
                return settled;
        }
        var added = invoice.AddManualLine(type, description, quantity, unitPrice, amount, note, feeTypeId,
            roomChargeId: charge.Id, isSettled: charge.IsSettled);
        if (added.IsFailure)
            return Result.Failure(added.Error!);
        charge.AttachTo(invoice.Id);
        charges.Add(charge);
        return Result.Success();
    }

    public async Task DetachAllAsync(Guid invoiceId, CancellationToken ct)
    {
        foreach (var charge in await charges.ListByInvoiceAsync(invoiceId, ct))
            charge.Detach();
    }

    /// <summary>Nháp của HĐ nhận được khoản phát sinh ngày <paramref name="incurredOn"/> (BL-BR-32) — khóa rồi nạp kèm dòng để sửa.</summary>
    public async Task<Invoice?> DraftForAsync(Guid contractId, DateOnly incurredOn, CancellationToken ct)
    {
        var id = await invoiceQueries.FindDraftIdForChargeAsync(contractId, incurredOn, ct);
        if (id is null)
            return null;
        await unitOfWork.LockForUpdateAsync<Invoice>(id.Value, ct);
        return await invoices.GetForUpdateAsync(id.Value, ct);
    }

    /// <summary>
    /// Khóa HĐ → phiếu đang chứa khoản, chặn khi phiếu đã chốt (BL-BR-31/35), chạy thao tác trên khoản + nháp, lưu.
    /// </summary>
    public async Task<Result<RoomChargeDto>> MutateAsync(Guid chargeId, Func<RoomCharge, Invoice?, Result> action, CancellationToken ct)
    {
        var contractId = await chargeQueries.GetContractIdAsync(chargeId, ct);
        if (contractId is null)
            return RoomChargeErrors.NotFound;

        await using var transaction = await unitOfWork.BeginTransactionAsync(ct);
        await unitOfWork.LockForUpdateAsync<Contract>(contractId.Value, ct);
        var charge = (await charges.GetForUpdateAsync(chargeId, ct))!;
        Invoice? draft = null;
        if (charge.InvoiceId is { } invoiceId)
        {
            await unitOfWork.LockForUpdateAsync<Invoice>(invoiceId, ct);
            draft = await invoices.GetForUpdateAsync(invoiceId, ct);
            if (draft is not { Status: InvoiceStatus.Draft })
                return RoomChargeErrors.Locked;
        }

        var result = action(charge, draft);
        if (result.IsFailure)
            return result.Error!;
        await unitOfWork.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return (await ToDtosAsync([charge], ct))[0];
    }

    public async Task<IReadOnlyList<RoomChargeDto>> ToDtosAsync(IReadOnlyList<RoomCharge> roomCharges, CancellationToken ct)
    {
        var invoiceIds = roomCharges.Where(c => c.InvoiceId is not null).Select(c => c.InvoiceId!.Value).Distinct().ToList();
        var refs = await invoiceQueries.GetRefsAsync(invoiceIds, ct);
        var roomIds = roomCharges.Select(c => c.RoomId).Distinct().ToList();
        var codes = await rooms.GetCodesAsync(roomIds, ct);
        return roomCharges.Select(c =>
        {
            var invoice = c.InvoiceId is { } id ? refs.GetValueOrDefault(id) : null;
            var status = c.IsCancelled ? RoomChargeStatus.Cancelled
                : invoice is null ? RoomChargeStatus.Pending
                : invoice.Status == InvoiceStatus.Draft ? RoomChargeStatus.OnDraft
                : RoomChargeStatus.Billed;
            return new RoomChargeDto(c.Id, c.RoomId, codes.GetValueOrDefault(c.RoomId, ""), c.ContractId, c.Kind, c.Description, c.Amount,
                c.IncurredOn, c.Reason, c.IsSettled, c.SettledOn, c.SettledMethod, status, c.InvoiceId, invoice?.InvoiceNo,
                invoice is null ? null : $"{invoice.BillingMonth:yyyy-MM}", c.CancelReason, c.CreatedAt, c.IsSystem, c.FeeTypeId, c.Quantity,
                c.UnitPrice);
        }).ToList();
    }
}
