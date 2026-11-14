using FluentValidation;
using Mediator;
using renting_room.Application.Billing;
using renting_room.Application.Common.Interfaces;
using renting_room.Application.Contracts;
using renting_room.Application.Common.Validation;
using renting_room.Domain.Billing;
using renting_room.Domain.Common;
using renting_room.Domain.Contracts;
using renting_room.Domain.Payments;

namespace renting_room.Application.Payments;

public sealed record ReversePaymentCommand(Guid PaymentId, string Reason) : IRequest<Result<PaymentDto>>;

public sealed class ReversePaymentCommandValidator : AbstractValidator<ReversePaymentCommand>
{
    public ReversePaymentCommandValidator() => RuleFor(x => x.Reason).RequiredText(500, "Lý do đảo");
}

public sealed class ReversePaymentHandler(
    IPaymentQueries paymentQueries, IInvoiceQueries invoiceQueries, IContractQueries contracts, IUnitOfWork unitOfWork, CollectionLedger ledger)
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
        // PM-BR-39: đảo phiếu thu có thể đảo kèm phiếu hoàn của cùng hóa đơn — trả phiếu được yêu cầu.
        var reversed = await ledger.ReverseAsync(request.PaymentId, request.Reason, cancellationToken);
        if (reversed.IsFailure)
            return reversed.Error!;

        await unitOfWork.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return (await PaymentMapping.ToDtosAsync(invoiceQueries, [reversed.Value![0]], cancellationToken))[0];
    }
}
