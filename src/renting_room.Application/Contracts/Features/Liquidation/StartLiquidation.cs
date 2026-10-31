using FluentValidation;
using Mediator;
using renting_room.Application.Billing;
using renting_room.Application.Common.Validation;
using renting_room.Application.Meters;
using renting_room.Domain.Common;
using renting_room.Domain.Contracts;
using renting_room.Domain.Meters;

namespace renting_room.Application.Contracts;

/// <param name="FinalPeriodCharge">Kỳ cuối (CT-BR-11) — null = thu trọn kỳ.</param>
public sealed record StartLiquidationCommand(
    Guid ContractId, DateOnly ActualEndDate, TerminationReason Reason, TerminationGround? Ground, string? Note,
    FinalPeriodCharge? FinalPeriodCharge = null, IReadOnlyList<MeterReadingInput>? FinalReadings = null)
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
public sealed class StartLiquidationHandler(
    ContractMutation mutation, IInvoiceQueries invoices, LiquidationDrafts drafts, ContractMeterReadings meterReadings, TimeProvider clock)
    : IRequestHandler<StartLiquidationCommand, Result>
{
    public ValueTask<Result> Handle(StartLiquidationCommand request, CancellationToken cancellationToken) =>
        new(mutation.RunAsync(request.ContractId, async contract =>
        {
            await drafts.DeleteAfterAsync(contract.Id, request.ActualEndDate, cancellationToken);
            if (await invoices.HasInvoiceAfterAsync(contract.Id, request.ActualEndDate, cancellationToken))
                return Result.Failure(ContractErrors.InvoiceAfterEndDate);
            var today = clock.GetUtcNow().ToBusinessDate();
            var started = contract.StartLiquidation(request.ActualEndDate, request.Reason, request.Ground, request.Note, today, request.FinalPeriodCharge);
            // H-02: bấm thanh lý = người thuê đã rời ⇒ chốt chỉ số cuối ngay. Ngày trả phòng tương lai: nhập sau, chỉ ghi nếu đã gửi.
            if (started.IsFailure || !ContractMeterReadings.FinalRequiredAt(contract, request.ActualEndDate)
                || (request.ActualEndDate > today && request.FinalReadings is not { Count: > 0 }))
                return started;
            return await meterReadings.RecordAsync(contract, ReadingKind.Final, request.ActualEndDate, request.FinalReadings, cancellationToken);
        }, cancellationToken));
}
