using FluentValidation;
using Mediator;
using renting_room.Application.Billing;
using renting_room.Application.Common.Interfaces;
using renting_room.Application.Common.Validation;
using renting_room.Application.Identity;
using renting_room.Application.Meters;
using renting_room.Application.Payments;
using renting_room.Domain.Billing;
using renting_room.Domain.Common;
using renting_room.Domain.Contracts;
using renting_room.Domain.Meters;
using renting_room.Domain.Payments;

namespace renting_room.Application.Contracts;

/// <param name="Settlement">Bắt buộc khi còn nợ; null mà còn nợ ⇒ 422 CONTRACT_HAS_DEBT (cảnh báo kèm số nợ).</param>
/// <param name="Method">Phương thức khi "Đã thu toàn bộ" — mặc định tiền mặt.</param>
/// <param name="Reason">Lý do bỏ nợ (bắt buộc khi WriteOff) / ghi chú phiếu thu.</param>
public sealed record CompleteLiquidationCommand(
    Guid ContractId, DebtSettlement? Settlement = null, PaymentMethod? Method = null, DateOnly? PaidAt = null, string? Reason = null)
    : IRequest<Result>;

public sealed class CompleteLiquidationCommandValidator : AbstractValidator<CompleteLiquidationCommand>
{
    public CompleteLiquidationCommandValidator(TimeProvider clock)
    {
        RuleFor(x => x.Settlement).IsInEnum().When(x => x.Settlement is not null);
        RuleFor(x => x.Method).IsInEnum().When(x => x.Method is not null);
        RuleFor(x => x.Reason).OptionalText(500);
        RuleFor(x => x.Reason).NotEmpty().When(x => x.Settlement == DebtSettlement.WriteOff)
            .WithErrorCode("REQUIRED").WithMessage("Nhập lý do bỏ nợ.");
        RuleFor(x => x.PaidAt).Must(d => d is null || d <= clock.GetUtcNow().ToBusinessDate())
            .WithErrorCode("INVALID_PAID_AT").WithMessage("Ngày thu không được ở tương lai.");
    }
}

/// <summary>
/// CT-BR-12: có chỉ số cuối mọi công tơ → không còn phiếu nháp → phiếu quyết toán đã chốt → không còn phiếu chờ hoàn → còn nợ thì
/// "Đã thu toàn bộ" / "Bỏ nợ" theo lựa chọn của chủ trọ → kết thúc HĐ, đóng người ở và xe. Cọc không chặn (chỉ nhắc — PM-BR-35).
/// </summary>
public sealed class CompleteLiquidationHandler(
    ContractMutation mutation, ContractMeterReadings meterReadings, IInvoiceQueries invoiceQueries, IUserQueries users, PaymentPosting posting, ICurrentUser currentUser, TimeProvider clock)
    : IRequestHandler<CompleteLiquidationCommand, Result>
{
    public ValueTask<Result> Handle(CompleteLiquidationCommand request, CancellationToken cancellationToken) =>
        new(mutation.RunAsync(request.ContractId, async contract =>
        {
            var now = clock.GetUtcNow();
            if (contract.Status != ContractStatus.Liquidating)
                return Result.Failure(ContractErrors.NotLiquidating);
            if (now.ToBusinessDate() < contract.ActualEndDate)
                return Result.Failure(ContractErrors.LiquidationBeforeEndDate);

            var missing = await meterReadings.MissingFinalAsync(contract, contract.ActualEndDate!.Value, cancellationToken);
            if (missing.Count > 0)
                return Result.Failure(MeterErrors.FinalReadingRequired(missing));
            if (await invoiceQueries.HasDraftAsync(contract.Id, cancellationToken))
                return Result.Failure(BillingErrors.DraftExists);
            if (!await invoiceQueries.HasFinalizedFinalAsync(contract.Id, cancellationToken))
                return Result.Failure(BillingErrors.FinalRequired);
            var refundDue = await invoiceQueries.GetRefundDueAsync(contract.Id, cancellationToken);
            if (refundDue > 0)
                return Result.Failure(BillingErrors.RefundPending(refundDue));

            var outstanding = await invoiceQueries.GetContractOutstandingAsync(contract.Id, cancellationToken);
            if (outstanding > 0)
            {
                if (request.Settlement is null)
                    return Result.Failure(BillingErrors.HasDebt(outstanding));
                var writeOff = request.Settlement == DebtSettlement.WriteOff;
                if (writeOff && !await WriteOffGuard.IsAllowedAsync(users, currentUser, cancellationToken))
                    return Result.Failure(WriteOffErrors.NotAllowed);
                var posted = await posting.PostAsync(contract, null,
                    request.Method ?? PaymentMethod.Cash, request.PaidAt ?? now.ToBusinessDate(), null, null, null,
                    writeOff ? request.Reason : request.Reason ?? "Thu toàn bộ khi trả phòng",
                    writeOff ? PaymentKind.WriteOff : PaymentKind.Receipt, cancellationToken);
                if (posted.IsFailure)
                    return Result.Failure(posted.Error!);
            }
            return contract.CompleteLiquidation(now);
        }, cancellationToken));
}
