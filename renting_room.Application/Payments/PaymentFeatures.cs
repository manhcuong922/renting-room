using FluentValidation;
using Mediator;
using Microsoft.EntityFrameworkCore;
using renting_room.Application.Common.Interfaces;
using renting_room.Application.Common.Validation;
using renting_room.Domain.Billing;
using renting_room.Domain.Common;
using renting_room.Domain.Contracts;
using renting_room.Domain.Payments;

namespace renting_room.Application.Payments;

public sealed record PaymentAllocationDto(Guid InvoiceId, string? InvoiceNo, decimal Amount);

public sealed record PaymentDto(
    Guid Id,
    string ReceiptNo,
    Guid ContractId,
    decimal Amount,
    PaymentMethod Method,
    DateOnly PaidAt,
    string? PayerName,
    string? Reference,
    string? Note,
    PaymentStatus Status,
    PaymentKind Kind,
    DateTimeOffset? ReversedAt,
    string? ReverseReason,
    IReadOnlyList<PaymentAllocationDto> Allocations);

internal static class PaymentMapping
{
    public static async Task<IReadOnlyList<PaymentDto>> ToDtosAsync(IAppDbContext db, IReadOnlyList<Payment> payments, CancellationToken ct)
    {
        var invoiceIds = payments.SelectMany(p => p.Allocations).Select(a => a.InvoiceId).Distinct().ToList();
        var numbers = await db.Invoices.AsNoTracking().Where(i => invoiceIds.Contains(i.Id))
            .ToDictionaryAsync(i => i.Id, i => i.InvoiceNo, ct);
        return payments.Select(p => new PaymentDto(p.Id, p.ReceiptNo, p.ContractId, p.Amount, p.Method, p.PaidAt, p.PayerName, p.Reference,
                p.Note, p.Status, p.Kind, p.ReversedAt, p.ReverseReason,
                p.Allocations.Select(a => new PaymentAllocationDto(a.InvoiceId, numbers.GetValueOrDefault(a.InvoiceId), a.Amount)).ToList()))
            .ToList();
    }
}

// ============================================================ Ghi thu

/// <param name="InvoiceId">Có ⇒ thu cho đúng phiếu này (nút "Đã thu"); null ⇒ phân bổ tự động phiếu cũ nhất trước (PM-BR-06).</param>
public sealed record RecordPaymentCommand(
    Guid ContractId, decimal Amount, PaymentMethod Method, DateOnly PaidAt, Guid? InvoiceId, string? PayerName, string? Reference, string? Note)
    : IRequest<Result<PaymentDto>>;

public sealed class RecordPaymentCommandValidator : AbstractValidator<RecordPaymentCommand>
{
    public RecordPaymentCommandValidator(TimeProvider clock)
    {
        RuleFor(x => x.Amount).Money(allowZero: false).LessThanOrEqualTo(Payment.MaxAmount).WithErrorCode("INVALID_AMOUNT");
        RuleFor(x => x.Method).IsInEnum();
        RuleFor(x => x.PaidAt).Must(d => d <= clock.GetUtcNow().ToBusinessDate())
            .WithErrorCode("INVALID_PAID_AT").WithMessage("Ngày thu không được ở tương lai.");
        RuleFor(x => x.PayerName).OptionalText(200);
        RuleFor(x => x.Reference).OptionalText(100);
        RuleFor(x => x.Note).OptionalText(500);
    }
}

/// <summary>
/// PM-UC-01 (đợt 1 — thu bằng tay): HĐ đang hiệu lực / thanh lý (PM-BR-13); phân bổ chỉ định 1 phiếu hoặc tự động phiếu cũ nhất trước.
/// Chưa có số dư có ⇒ không thu vượt số còn nợ.
/// </summary>
public sealed class RecordPaymentHandler(IAppDbContext db, IDocumentNumberGenerator numbers, ICurrentUser currentUser, TimeProvider clock)
    : IRequestHandler<RecordPaymentCommand, Result<PaymentDto>>
{
    public async ValueTask<Result<PaymentDto>> Handle(RecordPaymentCommand request, CancellationToken cancellationToken)
    {
        await using var transaction = await db.BeginTransactionAsync(cancellationToken);
        await db.LockForUpdateAsync<Contract>(request.ContractId, cancellationToken);
        var contract = await db.Contracts.AsNoTracking().FirstOrDefaultAsync(c => c.Id == request.ContractId, cancellationToken);
        if (contract is null)
            return ContractErrors.NotFound;
        if (contract.Status is not (ContractStatus.Active or ContractStatus.Liquidating))
            return PaymentErrors.ContractNotBillable;
        if (request.PaidAt < contract.StartDate.AddDays(-PaymentPosting.MaxDaysBeforeStart))
            return PaymentErrors.PaidBeforeContract;

        var payment = await PaymentPosting.PostAsync(db, numbers, currentUser, clock, contract, request.Amount, request.Method, request.PaidAt,
            request.InvoiceId, request.PayerName, request.Reference, request.Note, PaymentKind.Receipt, cancellationToken);
        if (payment.IsFailure)
            return payment.Error!;

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return (await PaymentMapping.ToDtosAsync(db, [payment.Value!], cancellationToken))[0];
    }
}

/// <summary>
/// Ghi phiếu thu và phân bổ trong transaction của người gọi (đã khóa HĐ): khóa các phiếu còn nợ theo id (C-07), cập nhật <c>paid_amount</c>
/// cùng lúc (PM-BR-05). <c>amount</c> null ⇒ đúng tổng còn nợ ("Đã thu toàn bộ" / bỏ nợ khi hoàn tất thanh lý).
/// </summary>
internal static class PaymentPosting
{
    public const int MaxDaysBeforeStart = 60;

    /// <summary>Còn nợ của HĐ — chỉ phiếu chưa thu đủ; phiếu tổng âm (phải hoàn) không bù trừ nợ.</summary>
    public static async Task<decimal> OutstandingAsync(IAppDbContext db, Guid contractId, CancellationToken ct) =>
        await db.Invoices.Where(i => i.ContractId == contractId && i.Status == InvoiceStatus.Finalized && i.PaidAmount < i.TotalAmount)
            .SumAsync(i => (decimal?)(i.TotalAmount - i.PaidAmount), ct) ?? 0;

    /// <summary>Số chủ trọ còn phải trả lại người thuê (phiếu đã chốt tổng âm, chưa xác nhận đã hoàn — BL-BR-27).</summary>
    public static async Task<decimal> RefundDueAsync(IAppDbContext db, Guid contractId, CancellationToken ct) =>
        -(await db.Invoices.Where(i => i.ContractId == contractId && i.Status == InvoiceStatus.Finalized && i.TotalAmount < 0 && i.RefundedOn == null)
            .SumAsync(i => (decimal?)i.TotalAmount, ct) ?? 0);

    public static async Task<Result<Payment>> PostAsync(
        IAppDbContext db, IDocumentNumberGenerator numbers, ICurrentUser currentUser, TimeProvider clock, Contract contract,
        decimal? amount, PaymentMethod method, DateOnly paidAt, Guid? invoiceId, string? payerName, string? reference, string? note,
        PaymentKind kind, CancellationToken ct)
    {
        var openIds = await db.Invoices.Where(i => i.ContractId == contract.Id && i.Status == InvoiceStatus.Finalized && i.PaidAmount < i.TotalAmount)
            .Select(i => i.Id).OrderBy(id => id).ToListAsync(ct);
        foreach (var id in openIds)
            await db.LockForUpdateAsync<Invoice>(id, ct);
        var open = await db.Invoices.Where(i => openIds.Contains(i.Id)).ToListAsync(ct);

        var total = amount ?? open.Sum(i => i.Outstanding);
        var allocations = await AllocateAsync(db, contract.Id, total, invoiceId, open, ct);
        if (allocations.IsFailure)
            return allocations.Error!;

        // PM-BR-15: năm của số phiếu thu = năm của ngày lập (không phải ngày thu) — ghi bù đầu năm không làm lộn thứ tự số.
        var receiptNo = await numbers.NextAsync(currentUser.OrganizationId!.Value, "PT", clock.GetUtcNow().ToBusinessDate().Year, ct);
        var payment = Payment.Record(contract.PropertyId, contract.Id, receiptNo, total, method, paidAt, payerName, reference, note,
            allocations.Value!, kind);
        foreach (var (id, part) in allocations.Value!)
            open.Single(i => i.Id == id).ApplyPayment(part, kind == PaymentKind.WriteOff);
        db.Payments.Add(payment);
        return payment;
    }

    private static async Task<Result<IReadOnlyList<(Guid InvoiceId, decimal Amount)>>> AllocateAsync(
        IAppDbContext db, Guid contractId, decimal amount, Guid? invoiceId, List<Invoice> open, CancellationToken ct)
    {
        if (invoiceId is { } targetId)
        {
            var target = open.FirstOrDefault(i => i.Id == targetId);
            if (target is null)
            {
                var status = await db.Invoices.Where(i => i.Id == targetId && i.ContractId == contractId)
                    .Select(i => (InvoiceStatus?)i.Status).FirstOrDefaultAsync(ct);
                return status is null ? BillingErrors.NotFound
                    : status == InvoiceStatus.Finalized ? PaymentErrors.ExceedsDebt
                    : BillingErrors.NotFinalized;
            }
            return amount > target.Outstanding
                ? PaymentErrors.ExceedsDebt
                : Result.Success<IReadOnlyList<(Guid, decimal)>>([(target.Id, amount)]);
        }

        if (open.Count == 0 || amount <= 0)
            return PaymentErrors.NoDebt;
        if (amount > open.Sum(i => i.Outstanding))
            return PaymentErrors.ExceedsDebt;

        // PM-BR-06: hạn thanh toán sớm nhất trước, rồi kỳ cũ hơn.
        var remaining = amount;
        var result = new List<(Guid, decimal)>();
        foreach (var invoice in open.OrderBy(i => i.DueDate).ThenBy(i => i.PeriodStart))
        {
            if (remaining == 0)
                break;
            var part = Math.Min(remaining, invoice.Outstanding);
            result.Add((invoice.Id, part));
            remaining -= part;
        }
        return result;
    }
}

// ============================================================ Đảo phiếu thu

public sealed record ReversePaymentCommand(Guid PaymentId, string Reason) : IRequest<Result<PaymentDto>>;

public sealed class ReversePaymentCommandValidator : AbstractValidator<ReversePaymentCommand>
{
    public ReversePaymentCommandValidator() => RuleFor(x => x.Reason).RequiredText(500, "Lý do đảo");
}

/// <summary>PM-UC-04 / PM-BR-07: đảo phiếu thu nhập sai — hủy phân bổ, giảm tiền đã thu của các phiếu báo.</summary>
public sealed class ReversePaymentHandler(IAppDbContext db, TimeProvider clock) : IRequestHandler<ReversePaymentCommand, Result<PaymentDto>>
{
    public async ValueTask<Result<PaymentDto>> Handle(ReversePaymentCommand request, CancellationToken cancellationToken)
    {
        var contractId = await db.Payments.Where(p => p.Id == request.PaymentId).Select(p => (Guid?)p.ContractId).FirstOrDefaultAsync(cancellationToken);
        if (contractId is null)
            return PaymentErrors.NotFound;

        await using var transaction = await db.BeginTransactionAsync(cancellationToken);
        await db.LockForUpdateAsync<Contract>(contractId.Value, cancellationToken);
        if (await db.Contracts.Where(c => c.Id == contractId).Select(c => c.Status).FirstAsync(cancellationToken) == ContractStatus.Ended)
            return PaymentErrors.ContractNotBillable;
        var payment = await db.Payments.Include(p => p.Allocations).FirstAsync(p => p.Id == request.PaymentId, cancellationToken);
        var reversed = payment.Reverse(request.Reason, clock.GetUtcNow());
        if (reversed.IsFailure)
            return reversed.Error!;

        foreach (var (invoiceId, _) in reversed.Value!.OrderBy(a => a.InvoiceId))
            await db.LockForUpdateAsync<Invoice>(invoiceId, cancellationToken);
        var ids = reversed.Value!.Select(a => a.InvoiceId).ToList();
        var invoices = await db.Invoices.Where(i => ids.Contains(i.Id)).ToDictionaryAsync(i => i.Id, cancellationToken);
        foreach (var (invoiceId, amount) in reversed.Value!)
            invoices[invoiceId].ApplyPayment(-amount, payment.Kind == PaymentKind.WriteOff);

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return (await PaymentMapping.ToDtosAsync(db, [payment], cancellationToken))[0];
    }
}

// ============================================================ Danh sách

public sealed record ListPaymentsQuery(Guid? ContractId, Guid? RoomId, Guid? InvoiceId) : IRequest<IReadOnlyList<PaymentDto>>;

public sealed class ListPaymentsHandler(IAppDbContext db) : IRequestHandler<ListPaymentsQuery, IReadOnlyList<PaymentDto>>
{
    private const int MaxRows = 500;

    public async ValueTask<IReadOnlyList<PaymentDto>> Handle(ListPaymentsQuery request, CancellationToken cancellationToken)
    {
        var query = db.Payments.AsNoTracking().Include(p => p.Allocations).AsQueryable();
        if (request.ContractId is { } contractId)
            query = query.Where(p => p.ContractId == contractId);
        if (request.RoomId is { } roomId)
            query = query.Where(p => db.Contracts.Any(c => c.Id == p.ContractId && c.RoomId == roomId));
        if (request.InvoiceId is { } invoiceId)
            query = query.Where(p => p.Allocations.Any(a => a.InvoiceId == invoiceId));

        var payments = await query.OrderByDescending(p => p.PaidAt).ThenByDescending(p => p.ReceiptNo).Take(MaxRows).ToListAsync(cancellationToken);
        return await PaymentMapping.ToDtosAsync(db, payments, cancellationToken);
    }
}
