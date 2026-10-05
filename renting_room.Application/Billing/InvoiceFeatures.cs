using FluentValidation;
using Mediator;
using Microsoft.EntityFrameworkCore;
using renting_room.Application.Common.Interfaces;
using renting_room.Application.Common.Models;
using renting_room.Application.Common.Validation;
using renting_room.Application.Contracts;
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
    DateOnly? DueDate,
    int ErrorCount,
    string Version);

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
    IReadOnlyList<InvoiceSegmentDto> Segments);

public sealed record InvoiceDetailDto(
    InvoiceSummaryDto Summary,
    decimal Subtotal,
    decimal DiscountTotal,
    DateOnly? IssueDate,
    IReadOnlyList<InvoiceLineDto> Lines,
    IReadOnlyList<InvoiceIssue> Issues,
    string? Note,
    DateTimeOffset? FinalizedAt,
    DateTimeOffset? VoidedAt,
    string? VoidReason);

internal static class InvoiceMapping
{
    public static InvoiceSummaryDto ToSummary(this Invoice i, DateOnly today) => new(
        i.Id, i.InvoiceNo, i.Type, i.Status, i.PaymentStatus(today), i.PropertyId, i.RoomId, i.SnapshotRoomCode, i.ContractId,
        i.SnapshotContractNo, i.SnapshotRepresentativeName, $"{i.BillingMonth:yyyy-MM}", i.PeriodStart, i.PeriodEnd,
        i.TotalAmount, i.PaidAmount, i.Outstanding, i.DueDate, i.Issues.Count(x => x.Severity == InvoiceIssue.Error), i.Version.ToString());

    public static InvoiceDetailDto ToDetail(this Invoice i, DateOnly today) => new(
        i.ToSummary(today), i.Subtotal, i.DiscountTotal, i.IssueDate,
        i.Lines.OrderBy(l => l.SortOrder).Select(l => new InvoiceLineDto(
            l.Id, l.Type, l.IsSystem, l.FeeTypeId, l.Description, l.Unit, l.ServiceFrom, l.ServiceTo, l.Quantity, l.UnitPrice,
            l.ProrationFactor, l.Amount, l.IsManuallyEdited, l.SystemQuantity, l.SystemUnitPrice, l.SystemAmount, l.Note,
            l.Type == InvoiceLineType.Metered && l.IsSystem
                ? i.Segments.Where(s => s.FeeTypeId == l.FeeTypeId)
                    .Select(s => new InvoiceSegmentDto(s.MeterId, s.StartReadingId, s.EndReadingId, s.StartValue, s.EndValue, s.Consumption)).ToList()
                : [])).ToList(),
        i.Issues, i.Note, i.FinalizedAt, i.VoidedAt, i.VoidReason);
}

internal static class InvoiceAccess
{
    public static Task<Invoice?> LoadAsync(IAppDbContext db, Guid id, CancellationToken ct) =>
        db.Invoices.Include(i => i.Lines).Include(i => i.Segments).AsSplitQuery().FirstOrDefaultAsync(i => i.Id == id, ct);

    /// <summary>Sửa nháp: khóa phiếu → nạp → thao tác → lưu, trong 1 transaction; trả chi tiết sau khi sửa.</summary>
    public static async Task<Result<InvoiceDetailDto>> MutateDraftAsync(
        IAppDbContext db, Guid invoiceId, DateOnly today, Func<Invoice, Result> action, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await db.LockForUpdateAsync<Invoice>(invoiceId, ct);
        var invoice = await LoadAsync(db, invoiceId, ct);
        if (invoice is null)
            return BillingErrors.NotFound;

        var result = action(invoice);
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
    bool? UnpaidOnly = null, int Page = 1, int PageSize = Paging.DefaultPageSize) : IRequest<PagedResult<InvoiceSummaryDto>>;

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
        return invoice is null ? BillingErrors.NotFound : invoice.ToDetail(clock.GetUtcNow().ToBusinessDate());
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
        await InvoiceAccess.MutateDraftAsync(db, request.InvoiceId, clock.GetUtcNow().ToBusinessDate(),
            invoice => invoice.EditLine(request.LineId, request.Quantity, request.UnitPrice, request.Amount, request.Note), cancellationToken);
}

/// <summary>Dòng hệ thống ⇒ bỏ sửa tay (về số hệ thống); phụ thu / giảm tay ⇒ xóa dòng.</summary>
public sealed record ResetInvoiceLineCommand(Guid InvoiceId, Guid LineId) : IRequest<Result<InvoiceDetailDto>>;

public sealed class ResetInvoiceLineHandler(IAppDbContext db, TimeProvider clock) : IRequestHandler<ResetInvoiceLineCommand, Result<InvoiceDetailDto>>
{
    public async ValueTask<Result<InvoiceDetailDto>> Handle(ResetInvoiceLineCommand request, CancellationToken cancellationToken) =>
        await InvoiceAccess.MutateDraftAsync(db, request.InvoiceId, clock.GetUtcNow().ToBusinessDate(),
            invoice => invoice.ResetLine(request.LineId), cancellationToken);
}

/// <param name="Type"><c>Surcharge</c> (phụ thu) hoặc <c>ManualDiscount</c> (giảm tay) — nhập số dương.</param>
public sealed record AddInvoiceManualLineCommand(
    Guid InvoiceId, InvoiceLineType Type, string Description, decimal? Quantity, decimal? UnitPrice, decimal Amount, string Note, Guid? FeeTypeId)
    : IRequest<Result<InvoiceDetailDto>>;

public sealed class AddInvoiceManualLineCommandValidator : AbstractValidator<AddInvoiceManualLineCommand>
{
    public AddInvoiceManualLineCommandValidator()
    {
        RuleFor(x => x.Type).Must(t => t is InvoiceLineType.Surcharge or InvoiceLineType.ManualDiscount)
            .WithErrorCode("INVALID_LINE_TYPE").WithMessage("Chỉ thêm phụ thu (Surcharge) hoặc giảm trừ (ManualDiscount).");
        RuleFor(x => x.Description).RequiredText(200, "Nội dung");
        RuleFor(x => x.Note).RequiredText(300, "Lý do");
        RuleFor(x => x.Amount).Money(allowZero: false).LessThanOrEqualTo(100_000_000).WithErrorCode("INVALID_AMOUNT");
        RuleFor(x => x.Quantity).Must(q => q is null || (q > 0 && decimal.Round(q.Value, 2) == q)).WithErrorCode("OUT_OF_RANGE");
        RuleFor(x => x.UnitPrice).Must(p => p is null || (p >= 0 && decimal.Round(p.Value, 2) == p)).WithErrorCode("INVALID_AMOUNT");
    }
}

/// <summary>BL-BR-23: phụ thu (sửa chữa do người thuê làm hỏng, lắp thêm, đền bù, tiền điện công tơ cũ khi thay công tơ…) / giảm tay.</summary>
public sealed class AddInvoiceManualLineHandler(IAppDbContext db, TimeProvider clock) : IRequestHandler<AddInvoiceManualLineCommand, Result<InvoiceDetailDto>>
{
    public async ValueTask<Result<InvoiceDetailDto>> Handle(AddInvoiceManualLineCommand request, CancellationToken cancellationToken) =>
        await InvoiceAccess.MutateDraftAsync(db, request.InvoiceId, clock.GetUtcNow().ToBusinessDate(), invoice =>
        {
            var added = invoice.AddManualLine(request.Type, request.Description, request.Quantity, request.UnitPrice, request.Amount,
                request.Note, request.FeeTypeId);
            return added.IsSuccess ? Result.Success() : Result.Failure(added.Error!);
        }, cancellationToken);
}

public sealed record UpdateInvoiceNoteCommand(Guid InvoiceId, string? Note) : IRequest<Result<InvoiceDetailDto>>;

public sealed class UpdateInvoiceNoteCommandValidator : AbstractValidator<UpdateInvoiceNoteCommand>
{
    public UpdateInvoiceNoteCommandValidator() => RuleFor(x => x.Note).OptionalText(1000);
}

public sealed class UpdateInvoiceNoteHandler(IAppDbContext db, TimeProvider clock) : IRequestHandler<UpdateInvoiceNoteCommand, Result<InvoiceDetailDto>>
{
    public async ValueTask<Result<InvoiceDetailDto>> Handle(UpdateInvoiceNoteCommand request, CancellationToken cancellationToken) =>
        await InvoiceAccess.MutateDraftAsync(db, request.InvoiceId, clock.GetUtcNow().ToBusinessDate(), invoice =>
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
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await db.LockForUpdateAsync<Invoice>(request.Id, cancellationToken);
        var invoice = await InvoiceAccess.LoadAsync(db, request.Id, cancellationToken);
        if (invoice is null)
            return Result.Failure(BillingErrors.NotFound);
        if (invoice.Status != InvoiceStatus.Draft)
            return Result.Failure(BillingErrors.NotDraft);
        if (await InvoiceAccess.HasLaterInvoiceAsync(db, invoice, cancellationToken))
            return Result.Failure(BillingErrors.NotLatest);

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

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await db.LockForUpdateAsync<Contract>(contractId.Value, ct);
        await db.LockForUpdateAsync<Invoice>(id, ct);
        var contract = (await ContractMutation.LoadAsync(db, contractId.Value, ct))!;
        var invoice = (await InvoiceAccess.LoadAsync(db, id, ct))!;
        if (invoice.Status != InvoiceStatus.Draft)
            return BillingErrors.NotDraft;

        var period = InvoiceInputs.CurrentPeriod(contract, invoice);
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
        var finalized = invoice.Finalize(invoiceNo, today, contract.PaymentDueDays, now);
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

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await db.LockForUpdateAsync<Contract>(contractId.Value, cancellationToken);
        await db.LockForUpdateAsync<Invoice>(request.Id, cancellationToken);
        var invoice = (await InvoiceAccess.LoadAsync(db, request.Id, cancellationToken))!;
        if (invoice.Status == InvoiceStatus.Finalized && await InvoiceAccess.HasLaterInvoiceAsync(db, invoice, cancellationToken))
            return BillingErrors.NotLatest;

        var now = clock.GetUtcNow();
        var voided = invoice.Void(request.Reason, now);
        if (voided.IsFailure)
            return voided.Error!;
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return invoice.ToDetail(now.ToBusinessDate());
    }
}
