using FluentValidation;
using Mediator;
using Microsoft.EntityFrameworkCore;
using renting_room.Application.Common.Interfaces;
using renting_room.Application.Common.Validation;
using renting_room.Domain.Billing;
using renting_room.Domain.Common;
using renting_room.Domain.Contracts;
using renting_room.Domain.Properties;

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

public sealed record RoomChargeDto(
    Guid Id, Guid RoomId, string RoomCode, Guid ContractId, RoomChargeKind Kind, string Description, decimal Amount, DateOnly IncurredOn,
    string Reason, bool IsSettled, DateOnly? SettledOn, PaymentMethod? SettledMethod, RoomChargeStatus Status, Guid? InvoiceId,
    string? InvoiceNo, string? BillingMonth, string? CancelReason, DateTimeOffset CreatedAt);

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
        var added = draft.AddManualLine(charge.LineType, charge.Description, null, null, charge.Amount, charge.Reason, null,
            roomChargeId: charge.Id, isSettled: charge.IsSettled);
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
                invoice is null ? null : $"{invoice.BillingMonth:yyyy-MM}", c.CancelReason, c.CreatedAt);
        }).ToList();
    }
}

// ============================================================ Tạo khoản phát sinh

/// <param name="Settlement">Có ⇒ khoản đã thanh toán / đã hoàn ngay (vẫn hiện trên phiếu, không tính vào tổng).</param>
public sealed record CreateRoomChargeCommand(
    Guid RoomId, RoomChargeKind Kind, string Description, decimal Amount, DateOnly IncurredOn, string Reason, ChargeSettlement? Settlement)
    : IRequest<Result<RoomChargeDto>>;

public sealed class CreateRoomChargeCommandValidator : AbstractValidator<CreateRoomChargeCommand>
{
    public CreateRoomChargeCommandValidator(TimeProvider clock)
    {
        RuleFor(x => x.Kind).IsInEnum();
        RuleFor(x => x.Description).RequiredText(200, "Nội dung");
        RuleFor(x => x.Reason).RequiredText(300, "Lý do");
        RuleFor(x => x.Amount).Money(allowZero: false).LessThanOrEqualTo(RoomCharge.MaxAmount).WithErrorCode("INVALID_AMOUNT");
        RuleFor(x => x.IncurredOn).Must(d => d <= clock.GetUtcNow().ToBusinessDate())
            .WithErrorCode(RoomChargeErrors.InvalidIncurredDate.Code).WithMessage(RoomChargeErrors.InvalidIncurredDate.Message);
        RuleFor(x => x.Settlement!.Method).IsInEnum().When(x => x.Settlement is not null);
    }
}

/// <summary>
/// BL-UC-15 / BL-BR-30..33: tạo phụ thu / bù cho phòng đang có người thuê tại ngày phát sinh; nháp nhận được khoản đã có ⇒ gắn ngay,
/// chưa có ⇒ chờ. Khóa HĐ → phiếu (C-07).
/// </summary>
public sealed class CreateRoomChargeHandler(IAppDbContext db, TimeProvider clock) : IRequestHandler<CreateRoomChargeCommand, Result<RoomChargeDto>>
{
    public async ValueTask<Result<RoomChargeDto>> Handle(CreateRoomChargeCommand request, CancellationToken cancellationToken)
    {
        var room = await db.Rooms.AsNoTracking().Where(r => r.Id == request.RoomId).Select(r => new { r.Id, r.PropertyId }).FirstOrDefaultAsync(cancellationToken);
        if (room is null)
            return PropertyErrors.RoomNotFound;

        // HĐ đang ở phòng tại ngày phát sinh — kể cả HĐ đã chuyển sang phòng khác sau đó (M05 CT-BR-14).
        var date = request.IncurredOn;
        var tenancy = await db.Contracts.AsNoTracking()
            .Where(c => (c.Status == ContractStatus.Active || c.Status == ContractStatus.Liquidating || c.Status == ContractStatus.Ended)
                && ((c.RoomId == room.Id && c.RoomSince <= date && (c.ActualEndDate == null || c.ActualEndDate >= date))
                    || c.RoomMoves.Any(m => m.RoomId == room.Id && m.FromDate <= date && m.ToDate >= date)))
            .OrderByDescending(c => c.StartDate).Select(c => new { c.Id, c.Status }).FirstOrDefaultAsync(cancellationToken);
        if (tenancy is null)
            return RoomChargeErrors.NoTenant;

        await using var transaction = await db.BeginTransactionAsync(cancellationToken);
        await db.LockForUpdateAsync<Contract>(tenancy.Id, cancellationToken);
        var status = await db.Contracts.Where(c => c.Id == tenancy.Id).Select(c => c.Status).FirstAsync(cancellationToken);
        var finalClosed = await db.Invoices.AnyAsync(
            i => i.ContractId == tenancy.Id && i.Type == InvoiceType.Final && i.Status == InvoiceStatus.Finalized, cancellationToken);
        if (status == ContractStatus.Ended || (status == ContractStatus.Liquidating && finalClosed))
            return RoomChargeErrors.NoOpenInvoice;

        var charge = RoomCharge.Create(room.PropertyId, room.Id, tenancy.Id, request.Kind, request.Description, request.Amount, date, request.Reason);
        if (request.Settlement is { } settlement)
        {
            var settled = charge.Settle(settlement.SettledOn, settlement.Method, clock.GetUtcNow().ToBusinessDate());
            if (settled.IsFailure)
                return settled.Error!;
        }
        db.RoomCharges.Add(charge);
        if (await RoomChargeBilling.DraftForAsync(db, tenancy.Id, date, cancellationToken) is { } draft)
            RoomChargeBilling.Attach(draft, charge);

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return (await RoomChargeBilling.ToDtosAsync(db, [charge], cancellationToken))[0];
    }
}

// ============================================================ Đánh dấu đã thanh toán / bỏ đánh dấu / hủy

internal static class RoomChargeMutation
{
    /// <summary>
    /// Khóa HĐ → phiếu đang chứa khoản, chặn khi phiếu đã chốt (BL-BR-31/35), chạy thao tác trên khoản + nháp, lưu.
    /// </summary>
    public static async Task<Result<RoomChargeDto>> RunAsync(
        IAppDbContext db, Guid chargeId, Func<RoomCharge, Invoice?, Result> action, CancellationToken ct)
    {
        var contractId = await db.RoomCharges.Where(c => c.Id == chargeId).Select(c => (Guid?)c.ContractId).FirstOrDefaultAsync(ct);
        if (contractId is null)
            return RoomChargeErrors.NotFound;

        await using var transaction = await db.BeginTransactionAsync(ct);
        await db.LockForUpdateAsync<Contract>(contractId.Value, ct);
        var charge = await db.RoomCharges.FirstAsync(c => c.Id == chargeId, ct);
        Invoice? draft = null;
        if (charge.InvoiceId is { } invoiceId)
        {
            await db.LockForUpdateAsync<Invoice>(invoiceId, ct);
            draft = await InvoiceAccess.LoadAsync(db, invoiceId, ct);
            if (draft is not { Status: InvoiceStatus.Draft })
                return RoomChargeErrors.Locked;
        }

        var result = action(charge, draft);
        if (result.IsFailure)
            return result.Error!;
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return (await RoomChargeBilling.ToDtosAsync(db, [charge], ct))[0];
    }
}

public sealed record SettleRoomChargeCommand(Guid Id, DateOnly SettledOn, PaymentMethod Method) : IRequest<Result<RoomChargeDto>>;

public sealed class SettleRoomChargeCommandValidator : AbstractValidator<SettleRoomChargeCommand>
{
    public SettleRoomChargeCommandValidator() => RuleFor(x => x.Method).IsInEnum();
}

/// <summary>BL-UC-16: đánh dấu khoản đã thanh toán / đã hoàn ngay ⇒ dòng trên nháp (nếu có) ra khỏi tổng.</summary>
public sealed class SettleRoomChargeHandler(IAppDbContext db, TimeProvider clock) : IRequestHandler<SettleRoomChargeCommand, Result<RoomChargeDto>>
{
    public async ValueTask<Result<RoomChargeDto>> Handle(SettleRoomChargeCommand request, CancellationToken cancellationToken) =>
        await RoomChargeMutation.RunAsync(db, request.Id, (charge, draft) =>
        {
            var settled = charge.Settle(request.SettledOn, request.Method, clock.GetUtcNow().ToBusinessDate());
            return settled.IsFailure || draft is null ? settled : draft.SetChargeSettled(charge.Id, true);
        }, cancellationToken);
}

public sealed record UnsettleRoomChargeCommand(Guid Id) : IRequest<Result<RoomChargeDto>>;

/// <summary>Bỏ đánh dấu đã thanh toán (nhập nhầm) ⇒ dòng trên nháp tính lại vào tổng.</summary>
public sealed class UnsettleRoomChargeHandler(IAppDbContext db) : IRequestHandler<UnsettleRoomChargeCommand, Result<RoomChargeDto>>
{
    public async ValueTask<Result<RoomChargeDto>> Handle(UnsettleRoomChargeCommand request, CancellationToken cancellationToken) =>
        await RoomChargeMutation.RunAsync(db, request.Id, (charge, draft) =>
        {
            var unsettled = charge.Unsettle();
            return unsettled.IsFailure || draft is null ? unsettled : draft.SetChargeSettled(charge.Id, false);
        }, cancellationToken);
}

public sealed record CancelRoomChargeCommand(Guid Id, string Reason) : IRequest<Result<RoomChargeDto>>;

public sealed class CancelRoomChargeCommandValidator : AbstractValidator<CancelRoomChargeCommand>
{
    public CancelRoomChargeCommandValidator() => RuleFor(x => x.Reason).RequiredText(300, "Lý do hủy");
}

/// <summary>BL-BR-35: hủy khoản chưa nằm trên phiếu đã chốt; đang trên nháp ⇒ xóa dòng.</summary>
public sealed class CancelRoomChargeHandler(IAppDbContext db, TimeProvider clock) : IRequestHandler<CancelRoomChargeCommand, Result<RoomChargeDto>>
{
    public async ValueTask<Result<RoomChargeDto>> Handle(CancelRoomChargeCommand request, CancellationToken cancellationToken) =>
        await RoomChargeMutation.RunAsync(db, request.Id, (charge, draft) =>
        {
            var removed = draft?.RemoveChargeLine(charge.Id) ?? Result.Success();
            return removed.IsFailure ? removed : charge.Cancel(request.Reason, clock.GetUtcNow());
        }, cancellationToken);
}

// ============================================================ Danh sách

public sealed record ListRoomChargesQuery(Guid? RoomId, Guid? PropertyId, RoomChargeStatus? Status, DateOnly? From, DateOnly? To)
    : IRequest<IReadOnlyList<RoomChargeDto>>;

/// <summary>BL-UC-17: khoản phát sinh theo phòng / khu, mới nhất trước — chủ trọ ở xa xem người quản lý đã ghi gì.</summary>
public sealed class ListRoomChargesHandler(IAppDbContext db) : IRequestHandler<ListRoomChargesQuery, IReadOnlyList<RoomChargeDto>>
{
    private const int MaxRows = 500;

    public async ValueTask<IReadOnlyList<RoomChargeDto>> Handle(ListRoomChargesQuery request, CancellationToken cancellationToken)
    {
        var query = db.RoomCharges.AsNoTracking().AsQueryable();
        if (request.RoomId is { } roomId)
            query = query.Where(c => c.RoomId == roomId);
        if (request.PropertyId is { } propertyId)
            query = query.Where(c => c.PropertyId == propertyId);
        if (request.From is { } from)
            query = query.Where(c => c.IncurredOn >= from);
        if (request.To is { } to)
            query = query.Where(c => c.IncurredOn <= to);
        query = request.Status switch
        {
            RoomChargeStatus.Cancelled => query.Where(c => c.CancelledAt != null),
            RoomChargeStatus.Pending => query.Where(c => c.CancelledAt == null && c.InvoiceId == null),
            RoomChargeStatus.OnDraft => query.Where(c => db.Invoices.Any(i => i.Id == c.InvoiceId && i.Status == InvoiceStatus.Draft)),
            RoomChargeStatus.Billed => query.Where(c => db.Invoices.Any(i => i.Id == c.InvoiceId && i.Status != InvoiceStatus.Draft)),
            _ => query
        };

        var charges = await query.OrderByDescending(c => c.IncurredOn).ThenByDescending(c => c.CreatedAt).Take(MaxRows).ToListAsync(cancellationToken);
        return await RoomChargeBilling.ToDtosAsync(db, charges, cancellationToken);
    }
}
