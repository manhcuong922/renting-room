using FluentValidation;
using Mediator;
using renting_room.Application.Billing;
using renting_room.Application.Common.Interfaces;
using renting_room.Application.Common.Validation;
using renting_room.Domain.Common;
using renting_room.Domain.Meters;

namespace renting_room.Application.Meters;

public sealed record VoidMeterReadingCommand(Guid ReadingId, string Reason) : IRequest<Result>;

public sealed class VoidMeterReadingCommandValidator : AbstractValidator<VoidMeterReadingCommand>
{
    public VoidMeterReadingCommandValidator() => RuleFor(x => x.Reason).RequiredText(300, "Lý do hủy");
}

public sealed class VoidMeterReadingHandler(
    IMeterRepository meters, IMeterQueries meterQueries, IInvoiceQueries invoices, IUnitOfWork unitOfWork, TimeProvider clock, MeterDraftRefresher drafts)
    : IRequestHandler<VoidMeterReadingCommand, Result>
{
    public async ValueTask<Result> Handle(VoidMeterReadingCommand request, CancellationToken cancellationToken)
    {
        var meterId = await meterQueries.GetMeterIdOfReadingAsync(request.ReadingId, cancellationToken);
        if (meterId is null)
            return Result.Failure(MeterErrors.ReadingNotFound);

        await using var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken);
        var affected = await drafts.LockContractsAsync([meterId.Value], cancellationToken);
        var meter = (await meters.LockAndLoadAsync([meterId.Value], cancellationToken)).Single();
        if ((await invoices.GetLockedReadingIdsAsync([meter.Id], cancellationToken)).Contains(request.ReadingId))
            return Result.Failure(MeterErrors.ReadingLocked);
        var voided = meter.VoidEntry(request.ReadingId, request.Reason, clock.GetUtcNow());
        if (voided.IsFailure)
            return voided;

        await drafts.RecalculateAsync(affected, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return voided;
    }
}
