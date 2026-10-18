using FluentValidation;
using Microsoft.EntityFrameworkCore;
using renting_room.Application.Common.Interfaces;
using renting_room.Domain.Billing;
using renting_room.Domain.Common;
using renting_room.Domain.Contracts;

namespace renting_room.Application.Billing;

/// <summary>Trạng thái hiển thị của khoản phát sinh (BL-UC-17) — dẫn xuất từ phiếu đang chứa nó.</summary>
public enum RoomChargeStatus
{
    /// <summary>Chờ vào phiếu.</summary>
    Pending,

    /// <summary>Đang trên phiếu nháp — còn sửa / hủy được.</summary>
    OnDraft,

    /// <summary>Đã vào phiếu đã chốt — khóa.</summary>
    Billed,
    Cancelled
}

/// <param name="IsSystem">BL-BR-38: khoản chốt điện nước khi chuyển phòng — không sửa / hủy tay; kèm khoản thu, sản lượng, đơn giá.</param>
public sealed record RoomChargeDto(
    Guid Id, Guid RoomId, string RoomCode, Guid ContractId, RoomChargeKind Kind, string Description, decimal Amount, DateOnly IncurredOn,
    string Reason, bool IsSettled, DateOnly? SettledOn, PaymentMethod? SettledMethod, RoomChargeStatus Status, Guid? InvoiceId,
    string? InvoiceNo, string? BillingMonth, string? CancelReason, DateTimeOffset CreatedAt, bool IsSystem = false, Guid? FeeTypeId = null,
    decimal? Quantity = null, decimal? UnitPrice = null);

/// <summary>"Đã thanh toán / đã hoàn ngay" khi tạo khoản hoặc thêm dòng tay trên nháp (BL-BR-31).</summary>
public sealed record ChargeSettlement(DateOnly SettledOn, PaymentMethod Method);

/// <summary>
/// M07 BL-BR-32..36 — gắn / gỡ khoản phát sinh với phiếu, trong transaction của người gọi (đã khóa HĐ / phiếu).
/// </summary>
internal static class RoomChargeBilling
{
    public const string NotAttachedCode = "ROOM_CHARGE_NOT_ATTACHED";

    /// <summary>
    /// Gắn các khoản đang chờ của HĐ vào nháp: ngày phát sinh ≤ ngày cuối kỳ (phiếu quyết toán: mọi khoản còn chờ). Khoản bù chưa trả làm phần thu
    /// âm ⇒ chưa gắn, nháp có cảnh báo (BL-BR-33).
    /// </summary>
    public static async Task AttachPendingAsync(IAppDbContext db, Invoice draft, CancellationToken ct)
    {
        var pending = await db.RoomCharges
            .Where(c => c.ContractId == draft.ContractId && c.InvoiceId == null && c.CancelledAt == null)
            .OrderBy(c => c.IncurredOn).ThenBy(c => c.CreatedAt).ToListAsync(ct);
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

    /// <summary>
    /// BL-BR-34: thêm dòng tay trên nháp. Phụ thu / giảm trừ ⇒ tạo luôn khoản phát sinh của phòng gắn vào nháp này (theo dõi được theo phòng,
    /// chọn đã thanh toán hay chưa); hoàn trả theo phiếu nguồn (BL-BR-27) là dòng thường.
    /// </summary>
    public static Result AddManualLine(
        IAppDbContext db, Invoice invoice, InvoiceLineType type, string description, decimal? quantity, decimal? unitPrice, decimal amount,
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
        db.RoomCharges.Add(charge);
        return Result.Success();
    }

    /// <summary>Nháp bị xóa / phiếu bị hủy ⇒ khoản quay về chờ (BL-BR-35).</summary>
    public static async Task DetachAllAsync(IAppDbContext db, Guid invoiceId, CancellationToken ct)
    {
        foreach (var charge in await db.RoomCharges.Where(c => c.InvoiceId == invoiceId).ToListAsync(ct))
            charge.Detach();
    }

    /// <summary>Nháp của HĐ nhận được khoản phát sinh ngày <paramref name="incurredOn"/> (BL-BR-32) — nạp kèm dòng để sửa.</summary>
    public static async Task<Invoice?> DraftForAsync(IAppDbContext db, Guid contractId, DateOnly incurredOn, CancellationToken ct)
    {
        var id = await db.Invoices
            .Where(i => i.ContractId == contractId && i.Status == InvoiceStatus.Draft && (i.Type == InvoiceType.Final || i.PeriodEnd >= incurredOn))
            .OrderBy(i => i.PeriodStart).Select(i => (Guid?)i.Id).FirstOrDefaultAsync(ct);
        if (id is null)
            return null;
        await db.LockForUpdateAsync<Invoice>(id.Value, ct);
        return await InvoiceAccess.LoadAsync(db, id.Value, ct);
    }

    public static async Task<IReadOnlyList<RoomChargeDto>> ToDtosAsync(IAppDbContext db, IReadOnlyList<RoomCharge> charges, CancellationToken ct)
    {
        var invoiceIds = charges.Where(c => c.InvoiceId is not null).Select(c => c.InvoiceId!.Value).Distinct().ToList();
        var invoices = await db.Invoices.AsNoTracking().Where(i => invoiceIds.Contains(i.Id))
            .Select(i => new { i.Id, i.InvoiceNo, i.Status, i.BillingMonth }).ToDictionaryAsync(i => i.Id, ct);
        var roomIds = charges.Select(c => c.RoomId).Distinct().ToList();
        var rooms = await db.Rooms.AsNoTracking().Where(r => roomIds.Contains(r.Id)).ToDictionaryAsync(r => r.Id, r => r.Code, ct);
        return charges.Select(c =>
        {
            var invoice = c.InvoiceId is { } id ? invoices.GetValueOrDefault(id) : null;
            var status = c.IsCancelled ? RoomChargeStatus.Cancelled
                : invoice is null ? RoomChargeStatus.Pending
                : invoice.Status == InvoiceStatus.Draft ? RoomChargeStatus.OnDraft
                : RoomChargeStatus.Billed;
            return new RoomChargeDto(c.Id, c.RoomId, rooms.GetValueOrDefault(c.RoomId, ""), c.ContractId, c.Kind, c.Description, c.Amount,
                c.IncurredOn, c.Reason, c.IsSettled, c.SettledOn, c.SettledMethod, status, c.InvoiceId, invoice?.InvoiceNo,
                invoice is null ? null : $"{invoice.BillingMonth:yyyy-MM}", c.CancelReason, c.CreatedAt, c.IsSystem, c.FeeTypeId, c.Quantity,
                c.UnitPrice);
        }).ToList();
    }
}
