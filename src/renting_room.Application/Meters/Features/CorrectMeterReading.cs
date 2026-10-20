using FluentValidation;
using Mediator;
using renting_room.Application.Billing;
using renting_room.Application.Common.Interfaces;
using renting_room.Application.Common.Validation;
using renting_room.Domain.Common;
using renting_room.Domain.Meters;

namespace renting_room.Application.Meters;

public sealed record CorrectMeterReadingCommand(Guid ReadingId, decimal Value, string? Note) : IRequest<Result<MeterReadingDto>>;

public sealed class CorrectMeterReadingCommandValidator : AbstractValidator<CorrectMeterReadingCommand>
{
    public CorrectMeterReadingCommandValidator()
    {
        RuleFor(x => x.Value).ReadingValue();
        RuleFor(x => x.Note).OptionalText(300);
    }
}

public sealed class CorrectMeterReadingHandler(IMeterRepository meters, IMeterQueries meterQueries, IInvoiceQueries invoices, IUnitOfWork unitOfWork, MeterDraftRefresher drafts)
    : IRequestHandler<CorrectMeterReadingCommand, Result<MeterReadingDto>>
{
    public async ValueTask<Result<MeterReadingDto>> Handle(CorrectMeterReadingCommand request, CancellationToken cancellationToken)
    {
        var meterId = await meterQueries.GetMeterIdOfReadingAsync(request.ReadingId, cancellationToken);
        if (meterId is null)
            return MeterErrors.ReadingNotFound;

        await using var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken);
        var affected = await drafts.LockContractsAsync([meterId.Value], cancellationToken);
        var meter = (await meters.LockAndLoadAsync([meterId.Value], cancellationToken)).Single();
        if ((await invoices.GetLockedReadingIdsAsync([meter.Id], cancellationToken)).Contains(request.ReadingId))
            return MeterErrors.ReadingLocked;
        var corrected = meter.Correct(request.ReadingId, request.Value, request.Note);
        if (corrected.IsFailure)
            return corrected.Error!;

        await drafts.RecalculateAsync(affected, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return corrected.Value!.ToDto();
    }
}
