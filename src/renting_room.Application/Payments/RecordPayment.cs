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
public sealed class RecordPaymentHandler(
    IContractQueries contracts, IInvoiceQueries invoiceQueries, IUnitOfWork unitOfWork, PaymentPosting posting)
    : IRequestHandler<RecordPaymentCommand, Result<PaymentDto>>
{
    public async ValueTask<Result<PaymentDto>> Handle(RecordPaymentCommand request, CancellationToken cancellationToken)
    {
        await using var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken);
        await unitOfWork.LockForUpdateAsync<Contract>(request.ContractId, cancellationToken);
        var contract = await contracts.GetAsync(request.ContractId, cancellationToken);
        if (contract is null)
            return ContractErrors.NotFound;
        if (contract.Status is not (ContractStatus.Active or ContractStatus.Liquidating))
            return PaymentErrors.ContractNotBillable;
        if (request.PaidAt < contract.StartDate.AddDays(-PaymentPosting.MaxDaysBeforeStart))
            return PaymentErrors.PaidBeforeContract;

        var payment = await posting.PostAsync(contract, request.Amount, request.Method, request.PaidAt,
            request.InvoiceId, request.PayerName, request.Reference, request.Note, PaymentKind.Receipt, cancellationToken);
        if (payment.IsFailure)
            return payment.Error!;

        await unitOfWork.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return (await PaymentMapping.ToDtosAsync(invoiceQueries, [payment.Value!], cancellationToken))[0];
    }
}
