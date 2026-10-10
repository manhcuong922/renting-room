using FluentValidation;
using Mediator;
using Microsoft.EntityFrameworkCore;
using renting_room.Application.Common.Interfaces;
using renting_room.Application.Common.Validation;
using renting_room.Domain.Billing;
using renting_room.Domain.Common;
using renting_room.Domain.Contracts;
using renting_room.Domain.Payments;

namespace renting_room.Application.Billing;

// ============================================================ Phụ thu / giảm trừ / hoàn trả cho nhiều phòng

/// <param name="RoomId">Phòng được chọn mà không có phiếu nháp ⇒ <see cref="InvoiceId"/> null, lỗi <c>NO_DRAFT_INVOICE</c>.</param>
public sealed record BulkManualLineOutcome(Guid? InvoiceId, Guid RoomId, string RoomCode, bool Success, string? ErrorCode, string? Message);

public sealed record BulkManualLineResult(int Added, IReadOnlyList<BulkManualLineOutcome> Results);

/// <summary>
/// Phạm vi: danh sách phiếu, hoặc khu + tháng thu (lọc phòng / tầng) — chỉ phiếu nháp. Một dòng giống nhau thêm vào từng phiếu.
/// </summary>
public sealed record BulkAddManualLineCommand(
    IReadOnlyList<Guid>? InvoiceIds, Guid? PropertyId, string? BillingMonth, IReadOnlyList<Guid>? RoomIds, string? Floor,
    InvoiceLineType Type, string Description, decimal? Quantity, decimal? UnitPrice, decimal Amount, string Note, Guid? FeeTypeId,
    ChargeSettlement? Settlement = null)
    : IRequest<Result<BulkManualLineResult>>;

public sealed class BulkAddManualLineCommandValidator : AbstractValidator<BulkAddManualLineCommand>
{
    public BulkAddManualLineCommandValidator(TimeProvider clock)
    {
        RuleFor(x => x).Must(x => x.InvoiceIds is { Count: > 0 } || (x.PropertyId is not null && x.BillingMonth is not null))
            .WithErrorCode("SCOPE_REQUIRED").WithMessage("Chọn phiếu, hoặc khu + tháng thu.");
        RuleFor(x => x.BillingMonth).BillingMonth(clock).When(x => x.BillingMonth is not null);
        RuleFor(x => x.InvoiceIds).Must(i => i is null || i.Count <= 1000).WithErrorCode("OUT_OF_RANGE");
        RuleFor(x => x.RoomIds).Must(r => r is null || r.Count <= 1000).WithErrorCode("OUT_OF_RANGE");
        ManualLineRules.Apply(this, x => x.Type, x => x.Description, x => x.Note, x => x.Amount, x => x.Quantity, x => x.UnitPrice);
        // BL-BR-27 (E): hoàn trả gắn phiếu nguồn riêng của từng phòng ⇒ không thêm hàng loạt.
        RuleFor(x => x.Type).NotEqual(InvoiceLineType.Refund)
            .WithErrorCode("REFUND_NOT_BULK").WithMessage("Hoàn trả thêm từng phiếu (chọn phiếu nguồn đã thu) — không thêm hàng loạt.");
    }
}

/// <summary>BL-UC-05: thêm phụ thu / giảm trừ / hoàn trả cho 1 phòng hay nhiều phòng một lúc — mỗi phiếu 1 transaction, trả kết quả từng phiếu.</summary>
public sealed class BulkAddManualLineHandler(IAppDbContext db, TimeProvider clock)
    : IRequestHandler<BulkAddManualLineCommand, Result<BulkManualLineResult>>
{
    public async ValueTask<Result<BulkManualLineResult>> Handle(BulkAddManualLineCommand request, CancellationToken cancellationToken)
    {
        var targets = await Scope(request).Select(i => new { i.Id, i.RoomId, i.SnapshotRoomCode, i.Status }).ToListAsync(cancellationToken);
        var results = new List<BulkManualLineOutcome>();
        foreach (var target in targets.OrderBy(t => t.SnapshotRoomCode))
        {
            var added = target.Status == InvoiceStatus.Draft
                ? await TryAddOneAsync(target.Id, request, cancellationToken)
                : Result.Failure(BillingErrors.NotDraft);
            results.Add(new BulkManualLineOutcome(target.Id, target.RoomId, target.SnapshotRoomCode, added.IsSuccess,
                added.Error?.Code, added.Error?.Message));
        }

        if (request.InvoiceIds is not { Count: > 0 } && request.RoomIds is { Count: > 0 } roomIds)
        {
            var covered = targets.Select(t => t.RoomId).ToHashSet();
            var missing = await db.Rooms.Where(r => roomIds.Contains(r.Id) && !covered.Contains(r.Id))
                .Select(r => new { r.Id, r.Code }).ToListAsync(cancellationToken);
            results.AddRange(missing.Select(r => new BulkManualLineOutcome(null, r.Id, r.Code, false, "NO_DRAFT_INVOICE",
                "Phòng chưa có phiếu nháp tháng này — tạo phiếu trước.")));
        }
        return new BulkManualLineResult(results.Count(r => r.Success), results);
    }

    private IQueryable<Invoice> Scope(BulkAddManualLineCommand request)
    {
        var query = db.Invoices.AsNoTracking().Where(i => i.Status != InvoiceStatus.Void);
        if (request.InvoiceIds is { Count: > 0 } ids)
            return query.Where(i => ids.Contains(i.Id));

        var month = BillingMonths.Parse(request.BillingMonth)!.Value;
        query = query.Where(i => i.PropertyId == request.PropertyId && i.BillingMonth == month);
        if (request.RoomIds is { Count: > 0 } roomIds)
            query = query.Where(i => roomIds.Contains(i.RoomId));
        if (!string.IsNullOrWhiteSpace(request.Floor))
            query = query.Where(i => db.Rooms.Any(r => r.Id == i.RoomId && r.Floor == request.Floor.Trim()));
        return query;
    }

    /// <summary>Lỗi lưu của 1 phiếu (xung đột, ràng buộc DB) thành kết quả của phiếu đó — các phiếu đã lưu trước vẫn giữ.</summary>
    private async Task<Result> TryAddOneAsync(Guid invoiceId, BulkAddManualLineCommand request, CancellationToken ct)
    {
        try
        {
            return await AddOneAsync(invoiceId, request, ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Result.Failure(BillingErrors.ConcurrencyConflict);
        }
        catch (DbUpdateException)
        {
            return Result.Failure(BillingErrors.SaveFailed);
        }
        finally
        {
            db.ChangeTracker.Clear();
        }
    }

    private async Task<Result> AddOneAsync(Guid invoiceId, BulkAddManualLineCommand request, CancellationToken ct)
    {
        await using var transaction = await db.BeginTransactionAsync(ct);
        await db.LockForUpdateAsync<Invoice>(invoiceId, ct);
        var invoice = await InvoiceAccess.LoadAsync(db, invoiceId, ct);
        if (invoice is null)
            return Result.Failure(BillingErrors.NotFound);
        var added = RoomChargeBilling.AddManualLine(db, invoice, request.Type, request.Description, request.Quantity, request.UnitPrice,
            request.Amount, request.Note, request.FeeTypeId, null, request.Settlement, clock.GetUtcNow().ToBusinessDate());
        if (added.IsFailure)
            return Result.Failure(added.Error!);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return Result.Success();
    }
}

// ============================================================ Xác nhận đã hoàn tiền (phiếu tổng âm)

public sealed record ConfirmInvoiceRefundCommand(Guid Id, DateOnly RefundedOn, PaymentMethod Method, string? Note) : IRequest<Result<InvoiceDetailDto>>;

public sealed class ConfirmInvoiceRefundCommandValidator : AbstractValidator<ConfirmInvoiceRefundCommand>
{
    public ConfirmInvoiceRefundCommandValidator()
    {
        RuleFor(x => x.Method).IsInEnum();
        RuleFor(x => x.Note).OptionalText(300);
    }
}

/// <summary>BL-BR-27: chủ trọ xác nhận đã trả lại người thuê phần tổng âm của phiếu đã chốt (ngày, cách hoàn, ghi chú).</summary>
public sealed class ConfirmInvoiceRefundHandler(IAppDbContext db, TimeProvider clock) : IRequestHandler<ConfirmInvoiceRefundCommand, Result<InvoiceDetailDto>>
{
    public async ValueTask<Result<InvoiceDetailDto>> Handle(ConfirmInvoiceRefundCommand request, CancellationToken cancellationToken)
    {
        var today = clock.GetUtcNow().ToBusinessDate();
        return await RefundMutation.RunAsync(db, request.Id, today,
            invoice => invoice.ConfirmRefund(request.RefundedOn, request.Method, request.Note, today), cancellationToken);
    }
}

public sealed record CancelInvoiceRefundCommand(Guid Id) : IRequest<Result<InvoiceDetailDto>>;

/// <summary>Bỏ xác nhận đã hoàn (nhập nhầm) — phải bỏ trước khi hủy phiếu.</summary>
public sealed class CancelInvoiceRefundHandler(IAppDbContext db, TimeProvider clock) : IRequestHandler<CancelInvoiceRefundCommand, Result<InvoiceDetailDto>>
{
    public async ValueTask<Result<InvoiceDetailDto>> Handle(CancelInvoiceRefundCommand request, CancellationToken cancellationToken) =>
        await RefundMutation.RunAsync(db, request.Id, clock.GetUtcNow().ToBusinessDate(),
            invoice => invoice.CancelRefund(), cancellationToken);
}

internal static class RefundMutation
{
    /// <summary>
    /// Khóa HĐ → phiếu (C-07, cùng thứ tự với hoàn tất thanh lý) rồi thao tác: hoàn tất thanh lý đọc số phải hoàn dưới khóa HĐ nên
    /// không lọt giữa chừng; HĐ đã kết thúc thì không đổi trạng thái hoàn tiền nữa.
    /// </summary>
    public static async Task<Result<InvoiceDetailDto>> RunAsync(
        IAppDbContext db, Guid invoiceId, DateOnly today, Func<Invoice, Result> action, CancellationToken ct)
    {
        var contractId = await db.Invoices.Where(i => i.Id == invoiceId).Select(i => (Guid?)i.ContractId).FirstOrDefaultAsync(ct);
        if (contractId is null)
            return BillingErrors.NotFound;

        await using var transaction = await db.BeginTransactionAsync(ct);
        await db.LockForUpdateAsync<Contract>(contractId.Value, ct);
        if (await db.Contracts.AnyAsync(c => c.Id == contractId && c.Status == ContractStatus.Ended, ct))
            return PaymentErrors.ContractNotBillable;
        await db.LockForUpdateAsync<Invoice>(invoiceId, ct);
        var invoice = (await InvoiceAccess.LoadAsync(db, invoiceId, ct))!;

        var result = action(invoice);
        if (result.IsFailure)
            return result.Error!;
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return invoice.ToDetail(today);
    }
}
