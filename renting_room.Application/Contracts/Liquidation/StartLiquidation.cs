using FluentValidation;
using Mediator;
using renting_room.Application.Common.Interfaces;
using renting_room.Application.Common.Validation;
using renting_room.Domain.Common;
using renting_room.Domain.Contracts;

namespace renting_room.Application.Contracts;

// Thanh lý
public sealed record StartLiquidationCommand(
    Guid ContractId, DateOnly ActualEndDate, TerminationReason Reason, TerminationGround? Ground, string? Note) : IRequest<Result>;

public sealed class StartLiquidationCommandValidator : AbstractValidator<StartLiquidationCommand>
{
    public StartLiquidationCommandValidator()
    {
        RuleFor(x => x.Reason).IsInEnum();
        RuleFor(x => x.Ground).IsInEnum().When(x => x.Ground is not null);
        RuleFor(x => x.Note).OptionalText(1000);
        RuleFor(x => x.Note)
            .NotEmpty().When(x => x.Ground == TerminationGround.Other)
            .WithErrorCode("REQUIRED").WithMessage("Căn cứ 'Khác' cần mô tả cụ thể.");
    }
}

public sealed class StartLiquidationHandler(IAppDbContext db, TimeProvider clock) : IRequestHandler<StartLiquidationCommand, Result>
{
    public ValueTask<Result> Handle(StartLiquidationCommand request, CancellationToken cancellationToken) =>
        new(ContractMutation.RunAsync(db, request.ContractId, async contract =>
            await LiquidationGuards.HasInvoiceAfterAsync(db, contract.Id, request.ActualEndDate, cancellationToken)
                ? Result.Failure(ContractErrors.InvoiceAfterEndDate)
                : contract.StartLiquidation(request.ActualEndDate, request.Reason, request.Ground, request.Note, clock.GetUtcNow().ToBusinessDate()),
            cancellationToken));
}
