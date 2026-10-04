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
                p.Note, p.Status, p.ReversedAt, p.ReverseReason,
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
/// PM-UC-01 (đợt 1 — thu bằng tay): khóa HĐ → các phiếu còn nợ (theo id) rồi phân bổ; cập nhật <c>paid_amount</c> trong cùng transaction
/// (PM-BR-05). Chưa có số dư có ⇒ không thu vượt số còn nợ.
/// </summary>
public sealed class RecordPaymentHandler(IAppDbContext db, IDocumentNumberGenerator numbers, ICurrentUser currentUser)
    : IRequestHandler<RecordPaymentCommand, Result<PaymentDto>>
{
    public async ValueTask<Result<PaymentDto>> Handle(RecordPaymentCommand request, CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await db.LockForUpdateAsync<Contract>(request.ContractId, cancellationToken);
        var contract = await db.Contracts.AsNoTracking().FirstOrDefaultAsync(c => c.Id == request.ContractId, cancellationToken);
        if (contract is null)
            return ContractErrors.NotFound;

        var openIds = await db.Invoices.Where(i => i.ContractId == contract.Id && i.Status == InvoiceStatus.Finalized && i.PaidAmount < i.TotalAmount)
            .Select(i => i.Id).OrderBy(id => id).ToListAsync(cancellationToken);
        foreach (var id in openIds)
            await db.LockForUpdateAsync<Invoice>(id, cancellationToken);
        var open = await db.Invoices.Where(i => openIds.Contains(i.Id)).ToListAsync(cancellationToken);

        var allocations = await AllocateAsync(request, open, cancellationToken);
        if (allocations.IsFailure)
            return allocations.Error!;

        var receiptNo = await numbers.NextAsync(currentUser.OrganizationId!.Value, "PT", request.PaidAt.Year, cancellationToken);
        var payment = Payment.Record(contract.PropertyId, contract.Id, receiptNo, request.Amount, request.Method, request.PaidAt,
            request.PayerName, request.Reference, request.Note, allocations.Value!);
        foreach (var (invoiceId, amount) in allocations.Value!)
            open.Single(i => i.Id == invoiceId).ApplyPayment(amount);
        db.Payments.Add(payment);

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return (await PaymentMapping.ToDtosAsync(db, [payment], cancellationToken))[0];
    }

    private async Task<Result<IReadOnlyList<(Guid InvoiceId, decimal Amount)>>> AllocateAsync(
        RecordPaymentCommand request, List<Invoice> open, CancellationToken ct)
    {
        if (request.InvoiceId is { } invoiceId)
        {
            var target = open.FirstOrDefault(i => i.Id == invoiceId);
            if (target is null)
            {
                var status = await db.Invoices.Where(i => i.Id == invoiceId && i.ContractId == request.ContractId)
                    .Select(i => (InvoiceStatus?)i.Status).FirstOrDefaultAsync(ct);
                return status is null ? BillingErrors.NotFound
                    : status == InvoiceStatus.Finalized ? PaymentErrors.ExceedsDebt
                    : BillingErrors.NotFinalized;
            }
            return request.Amount > target.Outstanding
                ? PaymentErrors.ExceedsDebt
                : Result.Success<IReadOnlyList<(Guid, decimal)>>([(target.Id, request.Amount)]);
        }

        if (open.Count == 0)
            return PaymentErrors.NoDebt;
        if (request.Amount > open.Sum(i => i.Outstanding))
            return PaymentErrors.ExceedsDebt;

        // PM-BR-06: hạn thanh toán sớm nhất trước, rồi kỳ cũ hơn.
        var remaining = request.Amount;
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

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await db.LockForUpdateAsync<Contract>(contractId.Value, cancellationToken);
        var payment = await db.Payments.Include(p => p.Allocations).FirstAsync(p => p.Id == request.PaymentId, cancellationToken);
        var reversed = payment.Reverse(request.Reason, clock.GetUtcNow());
        if (reversed.IsFailure)
            return reversed.Error!;

        foreach (var (invoiceId, _) in reversed.Value!.OrderBy(a => a.InvoiceId))
            await db.LockForUpdateAsync<Invoice>(invoiceId, cancellationToken);
        var ids = reversed.Value!.Select(a => a.InvoiceId).ToList();
        var invoices = await db.Invoices.Where(i => ids.Contains(i.Id)).ToDictionaryAsync(i => i.Id, cancellationToken);
        foreach (var (invoiceId, amount) in reversed.Value!)
            invoices[invoiceId].ApplyPayment(-amount);

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
