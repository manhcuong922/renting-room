using FluentValidation;
using Mediator;
using renting_room.Application.Billing;
using renting_room.Application.Common.Interfaces;
using renting_room.Application.Common.Validation;
using renting_room.Application.Contracts;
using renting_room.Domain.Billing;
using renting_room.Domain.Common;
using renting_room.Domain.Contracts;
using renting_room.Domain.Payments;

namespace renting_room.Application.Payments;

/// <summary>
/// M08 PM-UC-22 / PM-BR-38: sửa trực tiếp số "Đã thu" của hóa đơn đã chốt. <paramref name="ExpectedAmount"/> = số đã thu người dùng đang thấy.
/// Tăng ⇒ phiếu thu phần chênh (<paramref name="PaidAt"/> / <paramref name="Method"/>, mặc định hôm nay / tiền mặt); giảm ⇒ <paramref name="Reason"/> bắt buộc.
/// </summary>
public sealed record EditCollectedAmountCommand(
    Guid InvoiceId, decimal Amount, decimal ExpectedAmount, DateOnly? PaidAt, PaymentMethod? Method, string? Reason)
    : IRequest<Result<CollectedAmountResult>>;

/// <param name="Payments">Phiếu vừa ghi / đảo trong lần sửa này.</param>
public sealed record CollectedAmountResult(InvoiceSummaryDto Summary, IReadOnlyList<PaymentDto> Payments);

public sealed class EditCollectedAmountCommandValidator : AbstractValidator<EditCollectedAmountCommand>
{
    public EditCollectedAmountCommandValidator(TimeProvider clock)
    {
        RuleFor(x => x.Amount).Money(allowZero: true).LessThanOrEqualTo(Payment.MaxAmount).WithErrorCode("INVALID_AMOUNT");
        RuleFor(x => x.ExpectedAmount).Money(allowZero: true).LessThanOrEqualTo(Payment.MaxAmount).WithErrorCode("INVALID_AMOUNT");
        RuleFor(x => x.Method).IsInEnum();
        RuleFor(x => x.PaidAt).Must(d => d is null || d <= clock.GetUtcNow().ToBusinessDate())
            .WithErrorCode("INVALID_PAID_AT").WithMessage("Ngày thu không được ở tương lai.");
        RuleFor(x => x.Reason).RequiredText(500, "Lý do giảm số đã thu").When(x => x.Amount < x.ExpectedAmount);
        RuleFor(x => x.Reason).OptionalText(500).When(x => x.Amount >= x.ExpectedAmount);
    }
}

public sealed class EditCollectedAmountHandler(
    IInvoiceQueries invoiceQueries, IInvoiceRepository invoices, IContractQueries contracts, IUnitOfWork unitOfWork, PaymentPosting posting,
    CollectionLedger ledger, TimeProvider clock)
    : IRequestHandler<EditCollectedAmountCommand, Result<CollectedAmountResult>>
{
    public async ValueTask<Result<CollectedAmountResult>> Handle(EditCollectedAmountCommand request, CancellationToken cancellationToken)
    {
        var contractId = await invoiceQueries.GetContractIdAsync(request.InvoiceId, cancellationToken);
        if (contractId is null)
            return BillingErrors.NotFound;

        await using var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken);
        await unitOfWork.LockForUpdateAsync<Contract>(contractId.Value, cancellationToken);
        var contract = (await contracts.GetAsync(contractId.Value, cancellationToken))!;
        var invoice = (await invoices.LockForUpdateAsync([request.InvoiceId], cancellationToken))[request.InvoiceId];
        if (invoice.Status != InvoiceStatus.Finalized)
            return BillingErrors.NotFinalized;
        if (contract.Status is not (ContractStatus.Active or ContractStatus.Liquidating))
            return PaymentErrors.ContractNotBillable;
        if (invoice.CarriedToInvoiceId is not null)
            return BillingErrors.InvoiceCarriedOver;

        var current = invoice.CollectedAmount;
        if (request.ExpectedAmount != current)
            return PaymentErrors.CollectedAmountChanged;
        if (request.Amount == current)
            return PaymentErrors.CollectedAmountUnchanged;

        var today = clock.GetUtcNow().ToBusinessDate();
        IReadOnlyList<Payment> touched;
        if (request.Amount > current)
        {
            var paidAt = request.PaidAt ?? today;
            if (paidAt < contract.StartDate.AddDays(-PaymentPosting.MaxDaysBeforeStart))
                return PaymentErrors.PaidBeforeContract;
            var receipt = await posting.PostAsync(contract, request.Amount - current, request.Method ?? PaymentMethod.Cash, paidAt, invoice.Id, null, null,
                request.Reason, PaymentKind.Receipt, cancellationToken);
            if (receipt.IsFailure)
                return receipt.Error!;
            touched = [receipt.Value!];
        }
        else
        {
            // PM-BR-38 (6): hoàn trả trên phiếu sau đã trỏ vào hóa đơn này — không được rút số đã thu xuống dưới phần đã hoàn.
            var refunded = -await invoiceQueries.GetRefundLinesTotalAsync(invoice.Id, null, cancellationToken);
            if (request.Amount < refunded)
                return PaymentErrors.CollectedBelowRefunded(refunded);
            var reduced = await ledger.ReduceAsync(contract, invoice, request.Amount, request.Reason!, cancellationToken);
            if (reduced.IsFailure)
                return reduced.Error!;
            touched = reduced.Value!;
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new CollectedAmountResult(invoice.ToSummary(today), await PaymentMapping.ToDtosAsync(invoiceQueries, touched, cancellationToken));
    }
}
