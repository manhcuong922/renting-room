using System.Linq.Expressions;
using FluentValidation;
using Mediator;
using Microsoft.EntityFrameworkCore;
using renting_room.Application.Common.Interfaces;
using renting_room.Application.Common.Models;
using renting_room.Application.Common.Validation;
using renting_room.Application.Contracts;
using renting_room.Application.Properties;
using renting_room.Domain.Billing;
using renting_room.Domain.Common;
using renting_room.Domain.Contracts;

namespace renting_room.Application.Billing;

// ============================================================ DTO

public sealed record InvoiceSummaryDto(
    Guid Id,
    string? InvoiceNo,
    InvoiceType Type,
    InvoiceStatus Status,
    InvoicePaymentStatus? PaymentStatus,
    Guid PropertyId,
    Guid RoomId,
    string RoomCode,
    Guid ContractId,
    string ContractNo,
    string RepresentativeName,
    string BillingMonth,
    DateOnly PeriodStart,
    DateOnly PeriodEnd,
    decimal TotalAmount,
    decimal PaidAmount,
    decimal Outstanding,
    decimal RefundDue,
    DateOnly? DueDate,
    int ErrorCount,
    string Version,
    bool IsStale = false,
    decimal RoundingAmount = 0);

public sealed record InvoiceSegmentDto(Guid MeterId, Guid StartReadingId, Guid EndReadingId, decimal StartValue, decimal EndValue, decimal Consumption);

public sealed record InvoiceLineDto(
    Guid Id,
    InvoiceLineType Type,
    bool IsSystem,
    Guid? FeeTypeId,
    string Description,
    string? Unit,
    DateOnly ServiceFrom,
    DateOnly ServiceTo,
    decimal Quantity,
    decimal UnitPrice,
    decimal? ProrationFactor,
    decimal Amount,
    bool IsManuallyEdited,
    decimal? SystemQuantity,
    decimal? SystemUnitPrice,
    decimal? SystemAmount,
    string? Note,
    IReadOnlyList<InvoiceSegmentDto> Segments,
    Guid? SourceInvoiceId = null,
    Guid? RoomChargeId = null,
    bool IsSettled = false);

/// <summary>F1 (PM-UC-18): phiếu kỳ trước của cùng HĐ còn nợ — chỉ hiển thị, không cộng vào phiếu.</summary>
public sealed record PreviousDebtDto(Guid InvoiceId, string? InvoiceNo, string BillingMonth, DateOnly? DueDate, decimal Outstanding);

/// <param name="RefundTotal">Σ dòng hoàn trả (≤ 0).</param>
/// <param name="RefundedOn">Ngày chủ trọ xác nhận đã trả lại người thuê phần tổng âm (BL-BR-27).</param>
/// <param name="PreviousDebts">F1: "Nợ các kỳ trước" — chỉ có ở <c>GET /invoices/{id}</c>.</param>
/// <param name="TotalDue">F1: "Tổng cần thanh toán" = còn phải trả của phiếu này (nháp: tổng phiếu) + nợ các kỳ trước.</param>
public sealed record InvoiceDetailDto(
    InvoiceSummaryDto Summary,
    decimal Subtotal,
    decimal DiscountTotal,
    decimal RefundTotal,
    DateOnly? RefundedOn,
    PaymentMethod? RefundMethod,
    string? RefundNote,
    DateOnly? IssueDate,
    IReadOnlyList<InvoiceLineDto> Lines,
    IReadOnlyList<InvoiceIssue> Issues,
    string? Note,
    DateTimeOffset? FinalizedAt,
    DateTimeOffset? VoidedAt,
    string? VoidReason,
    IReadOnlyList<PreviousDebtDto>? PreviousDebts = null,
    decimal? TotalDue = null);

internal static class InvoiceMapping
{
    public static InvoiceSummaryDto ToSummary(this Invoice i, DateOnly today) => new(
        i.Id, i.InvoiceNo, i.Type, i.Status, i.PaymentStatus(today), i.PropertyId, i.RoomId, i.SnapshotRoomCode, i.ContractId,
        i.SnapshotContractNo, i.SnapshotRepresentativeName, $"{i.BillingMonth:yyyy-MM}", i.PeriodStart, i.PeriodEnd,
        i.TotalAmount, i.PaidAmount, i.Outstanding, i.RefundDue, i.DueDate, i.Issues.Count(x => x.Severity == InvoiceIssue.Error), i.Version.ToString(),
        i.IsStale, i.RoundingAmount);

    public static InvoiceDetailDto ToDetail(this Invoice i, DateOnly today) => new(
        i.ToSummary(today), i.Subtotal, i.DiscountTotal, i.RefundTotal, i.RefundedOn, i.RefundMethod, i.RefundNote, i.IssueDate,
        i.Lines.OrderBy(l => l.SortOrder).Select(l => new InvoiceLineDto(
            l.Id, l.Type, l.IsSystem, l.FeeTypeId, l.Description, l.Unit, l.ServiceFrom, l.ServiceTo, l.Quantity, l.UnitPrice,
            l.ProrationFactor, l.Amount, l.IsManuallyEdited, l.SystemQuantity, l.SystemUnitPrice, l.SystemAmount, l.Note,
            l.Type == InvoiceLineType.Metered && l.IsSystem
                ? i.Segments.Where(s => s.FeeTypeId == l.FeeTypeId)
                    .Select(s => new InvoiceSegmentDto(s.MeterId, s.StartReadingId, s.EndReadingId, s.StartValue, s.EndValue, s.Consumption)).ToList()
                : [], l.SourceInvoiceId, l.RoomChargeId, l.IsSettled)).ToList(),
        i.Issues, i.Note, i.FinalizedAt, i.VoidedAt, i.VoidReason);
}

internal static class InvoiceAccess
{
    public static Task<Invoice?> LoadAsync(IAppDbContext db, Guid id, CancellationToken ct) =>
        db.Invoices.Include(i => i.Lines).Include(i => i.Segments).AsSplitQuery().FirstOrDefaultAsync(i => i.Id == id, ct);

    /// <summary>Khóa phiếu → nạp → thao tác (domain tự kiểm trạng thái) → lưu, trong 1 transaction; trả chi tiết sau khi sửa.</summary>
    public static Task<Result<InvoiceDetailDto>> MutateAsync(
        IAppDbContext db, Guid invoiceId, DateOnly today, Func<Invoice, Result> action, CancellationToken ct) =>
        MutateAsync(db, invoiceId, today, invoice => Task.FromResult(action(invoice)), ct);

    public static async Task<Result<InvoiceDetailDto>> MutateAsync(
        IAppDbContext db, Guid invoiceId, DateOnly today, Func<Invoice, Task<Result>> action, CancellationToken ct)
    {
        await using var transaction = await db.BeginTransactionAsync(ct);
        await db.LockForUpdateAsync<Invoice>(invoiceId, ct);
        var invoice = await LoadAsync(db, invoiceId, ct);
        if (invoice is null)
            return BillingErrors.NotFound;

        var result = await action(invoice);
        if (result.IsFailure)
            return result.Error!;
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return invoice.ToDetail(today);
    }

    /// <summary>BL-BR-22: chỉ hủy / xóa phiếu mới nhất chưa hủy của HĐ.</summary>
    public static Task<bool> HasLaterInvoiceAsync(IAppDbContext db, Invoice invoice, CancellationToken ct) =>
        db.Invoices.AnyAsync(i => i.ContractId == invoice.ContractId && i.Status != InvoiceStatus.Void
            && i.Id != invoice.Id && i.PeriodStart > invoice.PeriodStart, ct);
}

// ============================================================ Truy vấn

public sealed record ListInvoicesQuery(
    Guid? PropertyId, string? BillingMonth, InvoiceStatus? Status, Guid? RoomId, Guid? ContractId, string? Floor,
    bool? UnpaidOnly = null, int Page = 1, int PageSize = Paging.DefaultPageSize, bool? Stale = null) : IRequest<PagedResult<InvoiceSummaryDto>>;

public sealed class ListInvoicesQueryValidator : AbstractValidator<ListInvoicesQuery>
{
    public ListInvoicesQueryValidator(TimeProvider clock)
    {
        RuleFor(x => x.Page).Page();
        RuleFor(x => x.PageSize).PageSize();
        RuleFor(x => x.BillingMonth).Must(v => BillingMonths.Parse(v) is not null).When(x => x.BillingMonth is not null)
            .WithErrorCode("INVALID_BILLING_MONTH").WithMessage("Tháng thu dạng yyyy-MM.");
    }
}

/// <summary>BL-UC-02 / BL-UC-14: danh sách phiếu theo khu / tháng / phòng / HĐ, mới nhất trước.</summary>
public sealed class ListInvoicesHandler(IAppDbContext db, TimeProvider clock) : IRequestHandler<ListInvoicesQuery, PagedResult<InvoiceSummaryDto>>
{
    public async ValueTask<PagedResult<InvoiceSummaryDto>> Handle(ListInvoicesQuery request, CancellationToken cancellationToken)
    {
        var query = db.Invoices.AsNoTracking();
        if (request.PropertyId is { } propertyId)
            query = query.Where(i => i.PropertyId == propertyId);
        if (BillingMonths.Parse(request.BillingMonth) is { } month)
            query = query.Where(i => i.BillingMonth == month);
        if (request.Status is { } status)
            query = query.Where(i => i.Status == status);
        if (request.RoomId is { } roomId)
            query = query.Where(i => i.RoomId == roomId);
        if (request.ContractId is { } contractId)
            query = query.Where(i => i.ContractId == contractId);
        if (!string.IsNullOrWhiteSpace(request.Floor))
            query = query.Where(i => db.Rooms.Any(r => r.Id == i.RoomId && r.Floor == request.Floor.Trim()));
        if (request.UnpaidOnly == true)
            query = query.Where(i => i.Status == InvoiceStatus.Finalized && i.PaidAmount < i.TotalAmount);
        if (request.Stale == true)
            query = query.Where(i => i.Status == InvoiceStatus.Draft && i.IsStale);

        var total = await query.CountAsync(cancellationToken);
        var items = await query.OrderByDescending(i => i.PeriodStart).ThenBy(i => i.SnapshotRoomCode)
            .Skip((request.Page - 1) * request.PageSize).Take(request.PageSize)
            .ToListAsync(cancellationToken);
        var today = clock.GetUtcNow().ToBusinessDate();
        return new PagedResult<InvoiceSummaryDto>(items.Select(i => i.ToSummary(today)).ToList(), request.Page, request.PageSize, total);
    }
}

public sealed record GetInvoiceQuery(Guid Id) : IRequest<Result<InvoiceDetailDto>>;

public sealed class GetInvoiceHandler(IAppDbContext db, TimeProvider clock) : IRequestHandler<GetInvoiceQuery, Result<InvoiceDetailDto>>
{
    public async ValueTask<Result<InvoiceDetailDto>> Handle(GetInvoiceQuery request, CancellationToken cancellationToken)
    {
        var invoice = await db.Invoices.AsNoTracking().Include(i => i.Lines).Include(i => i.Segments).AsSplitQuery()
            .FirstOrDefaultAsync(i => i.Id == request.Id, cancellationToken);
        if (invoice is null)
            return BillingErrors.NotFound;

        // F1 (PM-UC-18): phiếu kỳ trước của HĐ còn nợ — thu vẫn FIFO phiếu cũ nhất trước (PM-BR-06).
        var debts = await db.Invoices.AsNoTracking()
            .Where(i => i.ContractId == invoice.ContractId && i.Id != invoice.Id && i.Status == InvoiceStatus.Finalized
                && i.PaidAmount < i.TotalAmount && i.PeriodStart < invoice.PeriodStart)
            .OrderBy(i => i.DueDate).ThenBy(i => i.PeriodStart)
            .Select(i => new { i.Id, i.InvoiceNo, i.BillingMonth, i.DueDate, Outstanding = i.TotalAmount - i.PaidAmount })
            .ToListAsync(cancellationToken);
        var previous = debts.Select(d => new PreviousDebtDto(d.Id, d.InvoiceNo, $"{d.BillingMonth:yyyy-MM}", d.DueDate, d.Outstanding)).ToList();
        var own = invoice.Status == InvoiceStatus.Draft ? Math.Max(invoice.TotalAmount, 0) : invoice.Outstanding;
        return invoice.ToDetail(clock.GetUtcNow().ToBusinessDate()) with
        {
            PreviousDebts = previous, TotalDue = own + previous.Sum(d => d.Outstanding)
        };
    }
}

// ============================================================ Sửa nháp: sửa tay, phụ thu / giảm tay, ghi chú

/// <param name="Amount">Bỏ trống ⇒ số lượng × đơn giá × hệ số prorate.</param>
public sealed record EditInvoiceLineCommand(Guid InvoiceId, Guid LineId, decimal? Quantity, decimal? UnitPrice, decimal? Amount, string? Note)
    : IRequest<Result<InvoiceDetailDto>>;

public sealed class EditInvoiceLineCommandValidator : AbstractValidator<EditInvoiceLineCommand>
{
    public EditInvoiceLineCommandValidator()
    {
        RuleFor(x => x.Quantity).Must(q => q is null || (q >= 0 && q <= 99_999_999 && decimal.Round(q.Value, 2) == q))
            .WithErrorCode("OUT_OF_RANGE").WithMessage("Số lượng ≥ 0, tối đa 2 số lẻ.");
        RuleFor(x => x.UnitPrice).Must(p => p is null || (p >= 0 && p <= 1_000_000_000 && decimal.Round(p.Value, 2) == p))
            .WithErrorCode("INVALID_AMOUNT");
        RuleFor(x => x.Amount).OptionalMoney().Must(a => a is null || a <= 1_000_000_000).WithErrorCode("INVALID_AMOUNT");
        RuleFor(x => x.Note).OptionalText(300);
    }
}

public sealed class EditInvoiceLineHandler(IAppDbContext db, TimeProvider clock) : IRequestHandler<EditInvoiceLineCommand, Result<InvoiceDetailDto>>
{
    public async ValueTask<Result<InvoiceDetailDto>> Handle(EditInvoiceLineCommand request, CancellationToken cancellationToken) =>
        await InvoiceAccess.MutateAsync(db, request.InvoiceId, clock.GetUtcNow().ToBusinessDate(), async invoice =>
        {
            var edited = invoice.EditLine(request.LineId, request.Quantity, request.UnitPrice, request.Amount, request.Note);
            // BL-BR-34: sửa dòng phụ thu / giảm trừ trên nháp ⇒ sửa khoản phát sinh của phòng.
            if (edited.IsSuccess && invoice.Lines.First(l => l.Id == request.LineId) is { RoomChargeId: { } chargeId } line)
                (await db.RoomCharges.FirstAsync(c => c.Id == chargeId, cancellationToken)).Update(Math.Abs(line.Amount), line.Note!);
            return edited;
        }, cancellationToken);
}

/// <summary>Dòng hệ thống ⇒ bỏ sửa tay (về số hệ thống); phụ thu / giảm tay ⇒ xóa dòng.</summary>
public sealed record ResetInvoiceLineCommand(Guid InvoiceId, Guid LineId) : IRequest<Result<InvoiceDetailDto>>;

public sealed class ResetInvoiceLineHandler(IAppDbContext db, TimeProvider clock) : IRequestHandler<ResetInvoiceLineCommand, Result<InvoiceDetailDto>>
{
    public async ValueTask<Result<InvoiceDetailDto>> Handle(ResetInvoiceLineCommand request, CancellationToken cancellationToken) =>
        await InvoiceAccess.MutateAsync(db, request.InvoiceId, clock.GetUtcNow().ToBusinessDate(), async invoice =>
        {
            var chargeId = invoice.Lines.FirstOrDefault(l => l.Id == request.LineId)?.RoomChargeId;
            var reset = invoice.ResetLine(request.LineId);
            // BL-BR-34: xóa dòng phụ thu / giảm trừ trên nháp = hủy khoản phát sinh của phòng.
            if (reset.IsSuccess && chargeId is { } id)
                (await db.RoomCharges.FirstAsync(c => c.Id == id, cancellationToken)).Cancel("Xóa dòng trên phiếu nháp", clock.GetUtcNow());
            return reset;
        }, cancellationToken);
}

/// <param name="Type"><c>Surcharge</c> (phụ thu), <c>ManualDiscount</c> (giảm tay) hoặc <c>Refund</c> (hoàn trả) — nhập số dương.</param>
/// <param name="SourceInvoiceId">Bắt buộc với hoàn trả (BL-BR-27): phiếu đã chốt bị tính sai mà người thuê đã trả.</param>
/// <param name="Settlement">Phụ thu / giảm trừ đã thanh toán / đã hoàn ngay (BL-BR-34) ⇒ dòng hiện trên phiếu, không tính vào tổng.</param>
public sealed record AddInvoiceManualLineCommand(
    Guid InvoiceId, InvoiceLineType Type, string Description, decimal? Quantity, decimal? UnitPrice, decimal Amount, string Note, Guid? FeeTypeId,
    Guid? SourceInvoiceId = null, ChargeSettlement? Settlement = null)
    : IRequest<Result<InvoiceDetailDto>>;

public sealed class AddInvoiceManualLineCommandValidator : AbstractValidator<AddInvoiceManualLineCommand>
{
    public AddInvoiceManualLineCommandValidator()
    {
        ManualLineRules.Apply(this, x => x.Type, x => x.Description, x => x.Note, x => x.Amount, x => x.Quantity, x => x.UnitPrice);
        RuleFor(x => x.SourceInvoiceId).NotNull().When(x => x.Type == InvoiceLineType.Refund)
            .WithErrorCode(BillingErrors.RefundSourceRequired.Code).WithMessage(BillingErrors.RefundSourceRequired.Message);
    }
}

/// <summary>
/// BL-BR-27 (E — "chặn nhẹ"): hoàn trả ≤ số người thuê đã trả thật cho phiếu nguồn (đã thu − bỏ nợ) − các dòng hoàn trả khác đã trỏ tới phiếu
/// nguồn (phiếu chưa hủy). Gọi khi đã khóa phiếu nguồn ⇒ 2 lần hoàn song song không vượt.
/// </summary>
internal static class RefundSource
{
    public static async Task<Result> CheckAsync(IAppDbContext db, Invoice target, Guid sourceId, decimal amount, CancellationToken ct)
    {
        var source = await db.Invoices.AsNoTracking().FirstOrDefaultAsync(i => i.Id == sourceId, ct);
        if (source is null || source.ContractId != target.ContractId || source.Status != InvoiceStatus.Finalized)
            return Result.Failure(BillingErrors.RefundSourceInvalid);
        var refunded = -(await db.Invoices.Where(i => i.Status != InvoiceStatus.Void)
            .SelectMany(i => i.Lines).Where(l => l.SourceInvoiceId == sourceId && l.Type == InvoiceLineType.Refund)
            .SumAsync(l => (decimal?)l.Amount, ct) ?? 0);
        var available = Math.Max(source.PaidAmount - source.WrittenOffAmount - refunded, 0);
        return amount > available ? Result.Failure(BillingErrors.RefundExceedsPaid(available)) : Result.Success();
    }
}

internal static class ManualLineRules
{
    public const decimal MaxAmount = 100_000_000;

    /// <summary>BL-BR-23 / BL-BR-27: phụ thu / giảm tay / hoàn trả — nội dung, lý do bắt buộc, số tiền dương ≤ 100 triệu.</summary>
    public static void Apply<T>(
        AbstractValidator<T> v, Expression<Func<T, InvoiceLineType>> type, Expression<Func<T, string>> description,
        Expression<Func<T, string>> note, Expression<Func<T, decimal>> amount, Expression<Func<T, decimal?>> quantity,
        Expression<Func<T, decimal?>> unitPrice)
    {
        v.RuleFor(type).Must(Invoice.IsManualType)
            .WithErrorCode("INVALID_LINE_TYPE").WithMessage("Chỉ thêm phụ thu (Surcharge), giảm trừ (ManualDiscount) hoặc hoàn trả (Refund).");
        v.RuleFor(description).RequiredText(200, "Nội dung");
        v.RuleFor(note).RequiredText(300, "Lý do");
        v.RuleFor(amount).Money(allowZero: false).LessThanOrEqualTo(MaxAmount).WithErrorCode("INVALID_AMOUNT");
        v.RuleFor(quantity).Must(q => q is null || (q > 0 && decimal.Round(q.Value, 2) == q)).WithErrorCode("OUT_OF_RANGE");
        v.RuleFor(unitPrice).Must(p => p is null || (p >= 0 && decimal.Round(p.Value, 2) == p)).WithErrorCode("INVALID_AMOUNT");
    }
}

/// <summary>BL-BR-23: phụ thu (sửa chữa do người thuê làm hỏng, lắp thêm, đền bù, tiền điện công tơ cũ khi thay công tơ…) / giảm tay.</summary>
public sealed class AddInvoiceManualLineHandler(IAppDbContext db, TimeProvider clock) : IRequestHandler<AddInvoiceManualLineCommand, Result<InvoiceDetailDto>>
{
    public async ValueTask<Result<InvoiceDetailDto>> Handle(AddInvoiceManualLineCommand request, CancellationToken cancellationToken)
    {
        await using var transaction = await db.BeginTransactionAsync(cancellationToken);
        foreach (var id in new[] { request.InvoiceId, request.SourceInvoiceId ?? request.InvoiceId }.Distinct().Order())
            await db.LockForUpdateAsync<Invoice>(id, cancellationToken);
        var invoice = await InvoiceAccess.LoadAsync(db, request.InvoiceId, cancellationToken);
        if (invoice is null)
            return BillingErrors.NotFound;
        if (request.Type == InvoiceLineType.Refund && request.SourceInvoiceId is { } sourceId && sourceId != invoice.Id)
        {
            var allowed = await RefundSource.CheckAsync(db, invoice, sourceId, request.Amount, cancellationToken);
            if (allowed.IsFailure)
                return allowed.Error!;
        }

        var today = clock.GetUtcNow().ToBusinessDate();
        var added = RoomChargeBilling.AddManualLine(db, invoice, request.Type, request.Description, request.Quantity, request.UnitPrice,
            request.Amount, request.Note, request.FeeTypeId, request.SourceInvoiceId, request.Settlement, today);
        if (added.IsFailure)
            return added.Error!;
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return invoice.ToDetail(today);
    }
}

public sealed record UpdateInvoiceNoteCommand(Guid InvoiceId, string? Note) : IRequest<Result<InvoiceDetailDto>>;

public sealed class UpdateInvoiceNoteCommandValidator : AbstractValidator<UpdateInvoiceNoteCommand>
{
    public UpdateInvoiceNoteCommandValidator() => RuleFor(x => x.Note).OptionalText(1000);
}

public sealed class UpdateInvoiceNoteHandler(IAppDbContext db, TimeProvider clock) : IRequestHandler<UpdateInvoiceNoteCommand, Result<InvoiceDetailDto>>
{
    public async ValueTask<Result<InvoiceDetailDto>> Handle(UpdateInvoiceNoteCommand request, CancellationToken cancellationToken) =>
        await InvoiceAccess.MutateAsync(db, request.InvoiceId, clock.GetUtcNow().ToBusinessDate(), invoice =>
        {
            if (invoice.Status != InvoiceStatus.Draft)
                return Result.Failure(BillingErrors.NotDraft);
            invoice.UpdateNote(request.Note);
            return Result.Success();
        }, cancellationToken);
}

// ============================================================ Xóa nháp / chốt / hủy

public sealed record DeleteDraftInvoiceCommand(Guid Id) : IRequest<Result>;

public sealed class DeleteDraftInvoiceHandler(IAppDbContext db) : IRequestHandler<DeleteDraftInvoiceCommand, Result>
{
    public async ValueTask<Result> Handle(DeleteDraftInvoiceCommand request, CancellationToken cancellationToken)
    {
        await using var transaction = await db.BeginTransactionAsync(cancellationToken);
        await db.LockForUpdateAsync<Invoice>(request.Id, cancellationToken);
        var invoice = await InvoiceAccess.LoadAsync(db, request.Id, cancellationToken);
        if (invoice is null)
            return Result.Failure(BillingErrors.NotFound);
        if (invoice.Status != InvoiceStatus.Draft)
            return Result.Failure(BillingErrors.NotDraft);
        if (await InvoiceAccess.HasLaterInvoiceAsync(db, invoice, cancellationToken))
            return Result.Failure(BillingErrors.NotLatest);

        // BL-BR-35: khoản phát sinh của nháp quay về chờ.
        await RoomChargeBilling.DetachAllAsync(db, invoice.Id, cancellationToken);
        db.Invoices.Remove(invoice);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return Result.Success();
    }
}

public sealed record FinalizeInvoiceCommand(Guid Id) : IRequest<Result<InvoiceDetailDto>>;

/// <summary>
/// BL-BR-11/12/13: khóa HĐ → phiếu (C-07); tính lại phần hệ thống và so với nháp (bỏ qua ô sửa tay) — lệch ⇒ 409 DRAFT_STALE;
/// còn vấn đề chặn ⇒ 422; cấp số PB, hạn thanh toán = hôm nay + số ngày của HĐ.
/// </summary>
public sealed class FinalizeInvoiceHandler(IAppDbContext db, IDocumentNumberGenerator numbers, ICurrentUser currentUser, TimeProvider clock)
    : IRequestHandler<FinalizeInvoiceCommand, Result<InvoiceDetailDto>>
{
    public async ValueTask<Result<InvoiceDetailDto>> Handle(FinalizeInvoiceCommand request, CancellationToken cancellationToken) =>
        await FinalizeAsync(db, numbers, currentUser, clock, request.Id, cancellationToken);

    internal static async Task<Result<InvoiceDetailDto>> FinalizeAsync(
        IAppDbContext db, IDocumentNumberGenerator numbers, ICurrentUser currentUser, TimeProvider clock, Guid id, CancellationToken ct)
    {
        var contractId = await db.Invoices.Where(i => i.Id == id).Select(i => (Guid?)i.ContractId).FirstOrDefaultAsync(ct);
        if (contractId is null)
            return BillingErrors.NotFound;

        await using var transaction = await db.BeginTransactionAsync(ct);
        await db.LockForUpdateAsync<Contract>(contractId.Value, ct);
        await db.LockForUpdateAsync<Invoice>(id, ct);
        var contract = (await ContractMutation.LoadAsync(db, contractId.Value, ct))!;
        var invoice = (await InvoiceAccess.LoadAsync(db, id, ct))!;
        if (invoice.Status != InvoiceStatus.Draft)
            return BillingErrors.NotDraft;

        var billing = await PropertyBilling.LoadAsync(db, contract.PropertyId, ct);
        var period = InvoiceInputs.CurrentPeriod(contract, invoice, billing.Schedule);
        if (period is null || period.End != invoice.PeriodEnd)
            return BillingErrors.DraftStale;
        var calculation = InvoiceCalculator.Calculate(await InvoiceInputs.LoadAsync(db, contract, period, invoice.Id, ct, invoice.Type));
        if (!invoice.SystemPartMatches(calculation))
            return BillingErrors.DraftStale;

        var now = clock.GetUtcNow();
        var today = now.ToBusinessDate();
        if (invoice.HasBlockingIssues)
            return BillingErrors.HasIssues(invoice.Issues.Where(i => i.Severity == InvoiceIssue.Error).Select(i => i.Code).Distinct().ToList());
        var invoiceNo = await numbers.NextAsync(currentUser.OrganizationId!.Value, "PB", today.Year, ct);
        var finalized = invoice.Finalize(invoiceNo, today, billing.PaymentDueDays, now);
        if (finalized.IsFailure)
            return finalized.Error!;

        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return invoice.ToDetail(today);
    }
}

public sealed record FinalizeResult(Guid InvoiceId, bool Success, string? InvoiceNo, string? ErrorCode, string? Message);

public sealed record FinalizeInvoicesBatchCommand(IReadOnlyList<Guid> InvoiceIds) : IRequest<IReadOnlyList<FinalizeResult>>;

public sealed class FinalizeInvoicesBatchCommandValidator : AbstractValidator<FinalizeInvoicesBatchCommand>
{
    public FinalizeInvoicesBatchCommandValidator() =>
        RuleFor(x => x.InvoiceIds).NotEmpty().Must(i => i.Count <= 500).WithErrorCode("OUT_OF_RANGE");
}

/// <summary>BL-UC-07: chốt hàng loạt — mỗi phiếu 1 transaction, trả kết quả từng phiếu.</summary>
public sealed class FinalizeInvoicesBatchHandler(IAppDbContext db, IDocumentNumberGenerator numbers, ICurrentUser currentUser, TimeProvider clock)
    : IRequestHandler<FinalizeInvoicesBatchCommand, IReadOnlyList<FinalizeResult>>
{
    public async ValueTask<IReadOnlyList<FinalizeResult>> Handle(FinalizeInvoicesBatchCommand request, CancellationToken cancellationToken)
    {
        var results = new List<FinalizeResult>();
        foreach (var id in request.InvoiceIds.Distinct())
        {
            var result = await FinalizeInvoiceHandler.FinalizeAsync(db, numbers, currentUser, clock, id, cancellationToken);
            results.Add(result.IsSuccess
                ? new FinalizeResult(id, true, result.Value!.Summary.InvoiceNo, null, null)
                : new FinalizeResult(id, false, null, result.Error!.Code, result.Error.Message));
        }
        return results;
    }
}

public sealed record VoidInvoiceCommand(Guid Id, string Reason) : IRequest<Result<InvoiceDetailDto>>;

public sealed class VoidInvoiceCommandValidator : AbstractValidator<VoidInvoiceCommand>
{
    public VoidInvoiceCommandValidator() => RuleFor(x => x.Reason).RequiredText(300, "Lý do hủy");
}

/// <summary>BL-BR-15/22: hủy phiếu đã chốt chưa thu tiền, chỉ phiếu mới nhất của HĐ — chỉ số được giải phóng để lập lại.</summary>
public sealed class VoidInvoiceHandler(IAppDbContext db, TimeProvider clock) : IRequestHandler<VoidInvoiceCommand, Result<InvoiceDetailDto>>
{
    public async ValueTask<Result<InvoiceDetailDto>> Handle(VoidInvoiceCommand request, CancellationToken cancellationToken)
    {
        var contractId = await db.Invoices.Where(i => i.Id == request.Id).Select(i => (Guid?)i.ContractId).FirstOrDefaultAsync(cancellationToken);
        if (contractId is null)
            return BillingErrors.NotFound;

        await using var transaction = await db.BeginTransactionAsync(cancellationToken);
        await db.LockForUpdateAsync<Contract>(contractId.Value, cancellationToken);
        await db.LockForUpdateAsync<Invoice>(request.Id, cancellationToken);
        var invoice = (await InvoiceAccess.LoadAsync(db, request.Id, cancellationToken))!;
        if (invoice.Status == InvoiceStatus.Finalized && await InvoiceAccess.HasLaterInvoiceAsync(db, invoice, cancellationToken))
            return BillingErrors.NotLatest;

        var now = clock.GetUtcNow();
        var voided = invoice.Void(request.Reason, now);
        if (voided.IsFailure)
            return voided.Error!;
        // BL-BR-35: khoản phát sinh của phiếu bị hủy quay về chờ, vào phiếu lập lại.
        await RoomChargeBilling.DetachAllAsync(db, invoice.Id, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return invoice.ToDetail(now.ToBusinessDate());
    }
}
