using FluentValidation;
using Mediator;
using renting_room.Application.Billing;
using renting_room.Application.Common.Interfaces;
using renting_room.Application.Common.Validation;
using renting_room.Domain.Common;
using renting_room.Domain.Meters;

namespace renting_room.Application.Meters;

public sealed record ReplaceMeterCommand(
    Guid MeterId, DateOnly Date, decimal OldFinalValue, string? NewSerialNo, decimal NewInitialValue, string? Note) : IRequest<Result<Guid>>;

public sealed class ReplaceMeterCommandValidator : AbstractValidator<ReplaceMeterCommand>
{
    public ReplaceMeterCommandValidator(TimeProvider clock)
    {
        RuleFor(x => x.Date).ReadingDate(clock);
        RuleFor(x => x.OldFinalValue).ReadingValue();
        RuleFor(x => x.NewInitialValue).ReadingValue();
        RuleFor(x => x.NewSerialNo).OptionalText(50);
        RuleFor(x => x.Note).OptionalText(300);
    }
}

/// <summary>MT-UC-02 / MT-BR-09: thay công tơ (phiên bản) — số cuối công tơ cũ + số ban đầu công tơ mới, 1 transaction.</summary>
public sealed class ReplaceMeterHandler(IMeterRepository meters, IInvoiceQueries invoices, IUnitOfWork unitOfWork)
    : IRequestHandler<ReplaceMeterCommand, Result<Guid>>
{
    public async ValueTask<Result<Guid>> Handle(ReplaceMeterCommand request, CancellationToken cancellationToken)
    {
        await using var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken);
        var old = (await meters.LockAndLoadAsync([request.MeterId], cancellationToken)).FirstOrDefault();
        if (old is null)
            return MeterErrors.NotFound;
        if (MeterPeriodLocks.Check(await invoices.GetMeterLockedUntilAsync([old.Id], cancellationToken), old.Id, request.Date) is { } locked)
            return locked;

        var next = old.ReplaceWith(request.Date, request.OldFinalValue, request.NewSerialNo, request.NewInitialValue, request.Note);
        if (next.IsFailure)
            return next.Error!;

        // Lưu 2 lần: tháo bản cũ trước rồi mới lắp bản mới — unique "1 công tơ hoạt động" kiểm từng câu lệnh.
        await unitOfWork.SaveChangesAsync(cancellationToken);
        meters.Add(next.Value!);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return next.Value!.Id;
    }
}
