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

// Đảo phiếu thu
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
        var head = await db.Payments.Where(p => p.Id == request.PaymentId).Select(p => new { p.ContractId, p.Kind }).FirstOrDefaultAsync(cancellationToken);
        if (head is null)
            return PaymentErrors.NotFound;

        await using var transaction = await db.BeginTransactionAsync(cancellationToken);
        await db.LockForUpdateAsync<Contract>(head.ContractId, cancellationToken);
        if (await db.Contracts.Where(c => c.Id == head.ContractId).Select(c => c.Status).FirstAsync(cancellationToken) == ContractStatus.Ended)
            return PaymentErrors.ContractNotBillable;
        var payment = await PaymentReversal.ReverseAsync(db, request.PaymentId, request.Reason, clock.GetUtcNow(), cancellationToken);
        if (payment.IsFailure)
            return payment.Error!;

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return (await PaymentMapping.ToDtosAsync(db, [payment.Value!], cancellationToken))[0];
    }
}

/// <summary>Đảo phiếu thu trong transaction của người gọi (đã khóa HĐ): hủy phân bổ, khóa phiếu báo theo id rồi giảm tiền đã thu.</summary>
internal static class PaymentReversal
{
    public static async Task<Result<Payment>> ReverseAsync(IAppDbContext db, Guid paymentId, string reason, DateTimeOffset now, CancellationToken ct)
    {
        var payment = await db.Payments.Include(p => p.Allocations).FirstAsync(p => p.Id == paymentId, ct);
        var reversed = payment.Reverse(reason, now);
        if (reversed.IsFailure)
            return reversed.Error!;

        foreach (var (invoiceId, _) in reversed.Value!.OrderBy(a => a.InvoiceId))
            await db.LockForUpdateAsync<Invoice>(invoiceId, ct);
        var ids = reversed.Value!.Select(a => a.InvoiceId).ToList();
        var invoices = await db.Invoices.Where(i => ids.Contains(i.Id)).ToDictionaryAsync(i => i.Id, ct);
        foreach (var (invoiceId, amount) in reversed.Value!)
            invoices[invoiceId].ApplyPayment(-amount, payment.Kind == PaymentKind.WriteOff);
        return payment;
    }
}
