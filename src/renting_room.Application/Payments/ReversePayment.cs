using FluentValidation;
using Mediator;
using renting_room.Application.Billing;
using renting_room.Application.Common.Interfaces;
using renting_room.Application.Contracts;
using renting_room.Application.Common.Validation;
using renting_room.Domain.Common;
using renting_room.Domain.Contracts;
using renting_room.Domain.Payments;

namespace renting_room.Application.Payments;

public sealed record ReversePaymentCommand(Guid PaymentId, string Reason) : IRequest<Result<PaymentDto>>;

public sealed class ReversePaymentCommandValidator : AbstractValidator<ReversePaymentCommand>
{
    public ReversePaymentCommandValidator() => RuleFor(x => x.Reason).RequiredText(500, "Lý do đảo");
}

/// <summary>PM-UC-04 / PM-BR-07: đảo phiếu thu nhập sai — hủy phân bổ, giảm tiền đã thu của các phiếu báo.</summary>
public sealed class ReversePaymentHandler(
    IPaymentRepository payments, IPaymentQueries paymentQueries, IInvoiceRepository invoices, IInvoiceQueries invoiceQueries,
    IContractQueries contracts, IUnitOfWork unitOfWork, TimeProvider clock)
    : IRequestHandler<ReversePaymentCommand, Result<PaymentDto>>
{
    public async ValueTask<Result<PaymentDto>> Handle(ReversePaymentCommand request, CancellationToken cancellationToken)
    {
        var contractId = await paymentQueries.GetContractIdAsync(request.PaymentId, cancellationToken);
        if (contractId is null)
            return PaymentErrors.NotFound;

        await using var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken);
        await unitOfWork.LockForUpdateAsync<Contract>(contractId.Value, cancellationToken);
        if (await contracts.GetStatusAsync(contractId.Value, cancellationToken) == ContractStatus.Ended)
            return PaymentErrors.ContractNotBillable;
        var payment = await PaymentReversal.ReverseAsync(payments, invoices, request.PaymentId, request.Reason, clock.GetUtcNow(), cancellationToken);
        if (payment.IsFailure)
            return payment.Error!;

        await unitOfWork.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return (await PaymentMapping.ToDtosAsync(invoiceQueries, [payment.Value!], cancellationToken))[0];
    }
}

/// <summary>Đảo phiếu thu trong transaction của người gọi (đã khóa HĐ): hủy phân bổ, khóa phiếu báo theo id rồi giảm tiền đã thu.</summary>
internal static class PaymentReversal
{
    public static async Task<Result<Payment>> ReverseAsync(
        IPaymentRepository payments, IInvoiceRepository invoiceRepository, Guid paymentId, string reason, DateTimeOffset now, CancellationToken ct)
    {
        var payment = (await payments.GetWithAllocationsForUpdateAsync(paymentId, ct))!;
        var reversed = payment.Reverse(reason, now);
        if (reversed.IsFailure)
            return reversed.Error!;

        var ids = reversed.Value!.Select(a => a.InvoiceId).ToList();
        var invoices = await invoiceRepository.LockForUpdateAsync(ids, ct);
        foreach (var (invoiceId, amount) in reversed.Value!)
            invoices[invoiceId].ApplyPayment(-amount, payment.Kind == PaymentKind.WriteOff);
        return payment;
    }
}
