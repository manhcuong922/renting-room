using FluentValidation;
using Mediator;
using renting_room.Application.Billing;
using renting_room.Application.Common.Validation;
using renting_room.Domain.Common;
using renting_room.Domain.Contracts;

namespace renting_room.Application.Contracts;

/// <param name="FinalPeriodCharge">Kỳ cuối (CT-BR-11) — null = thu trọn kỳ.</param>
public sealed record StartLiquidationCommand(
    Guid ContractId, DateOnly ActualEndDate, TerminationReason Reason, TerminationGround? Ground, string? Note,
    FinalPeriodCharge? FinalPeriodCharge = null)
    : IRequest<Result>;

public sealed class StartLiquidationCommandValidator : AbstractValidator<StartLiquidationCommand>
{
    public StartLiquidationCommandValidator()
    {
        RuleFor(x => x.Reason).IsInEnum();
        RuleFor(x => x.Ground).IsInEnum().When(x => x.Ground is not null);
        RuleFor(x => x.FinalPeriodCharge).IsInEnum().When(x => x.FinalPeriodCharge is not null);
        RuleFor(x => x.Note).OptionalText(1000);
        RuleFor(x => x.Note)
            .NotEmpty().When(x => x.Ground == TerminationGround.Other)
            .WithErrorCode("REQUIRED").WithMessage("Căn cứ 'Khác' cần mô tả cụ thể.");
    }
}

/// <summary>
/// CT-BR-11 / CT-UC-08: nháp hóa đơn tháng của kỳ sau ngày trả phòng là kỳ ảo ⇒ xóa luôn (khoản phát sinh về chờ); hóa đơn đã chốt của kỳ
/// sau ngày trả phòng ⇒ chặn, chủ trọ tự hủy.
/// </summary>
public sealed class StartLiquidationHandler(ContractMutation mutation, IInvoiceQueries invoices, LiquidationDrafts drafts, TimeProvider clock)
    : IRequestHandler<StartLiquidationCommand, Result>
{
    public ValueTask<Result> Handle(StartLiquidationCommand request, CancellationToken cancellationToken) =>
        new(mutation.RunAsync(request.ContractId, async contract =>
        {
            await drafts.DeleteAfterAsync(contract.Id, request.ActualEndDate, cancellationToken);
            return await invoices.HasInvoiceAfterAsync(contract.Id, request.ActualEndDate, cancellationToken)
                ? Result.Failure(ContractErrors.InvoiceAfterEndDate)
                : contract.StartLiquidation(request.ActualEndDate, request.Reason, request.Ground, request.Note, clock.GetUtcNow().ToBusinessDate(),
                    request.FinalPeriodCharge);
        }, cancellationToken));
}
