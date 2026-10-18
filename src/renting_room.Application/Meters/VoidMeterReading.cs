using FluentValidation;
using Mediator;
using Microsoft.EntityFrameworkCore;
using renting_room.Application.Common.Interfaces;
using renting_room.Application.Common.Validation;
using renting_room.Domain.Common;
using renting_room.Domain.Meters;

namespace renting_room.Application.Meters;

// Sửa chỉ số
public sealed record VoidMeterReadingCommand(Guid ReadingId, string Reason) : IRequest<Result>;

public sealed class VoidMeterReadingCommandValidator : AbstractValidator<VoidMeterReadingCommand>
{
    public VoidMeterReadingCommandValidator() => RuleFor(x => x.Reason).RequiredText(300, "Lý do hủy");
}

/// <summary>MT-UC-06: hủy chỉ số ghi nhầm / ghi thừa (cuối kỳ, kiểm tra) chưa bị khóa — giữ dòng kèm lý do, nháp liên quan "Cần tính lại".</summary>
public sealed class VoidMeterReadingHandler(IAppDbContext db, TimeProvider clock) : IRequestHandler<VoidMeterReadingCommand, Result>
{
    public async ValueTask<Result> Handle(VoidMeterReadingCommand request, CancellationToken cancellationToken)
    {
        var meterId = await db.Meters.SelectMany(m => m.Readings).Where(r => r.Id == request.ReadingId)
            .Select(r => (Guid?)r.MeterId).FirstOrDefaultAsync(cancellationToken);
        if (meterId is null)
            return Result.Failure(MeterErrors.ReadingNotFound);

        await using var transaction = await db.BeginTransactionAsync(cancellationToken);
        var meter = (await MeterMapping.LockAndLoadAsync(db, [meterId.Value], cancellationToken)).Single();
        if ((await ReadingLocks.LockedReadingIdsAsync(db, [meter.Id], cancellationToken)).Contains(request.ReadingId))
            return Result.Failure(MeterErrors.ReadingLocked);
        var voided = meter.VoidEntry(request.ReadingId, request.Reason, clock.GetUtcNow());
        if (voided.IsFailure)
            return voided;

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return voided;
    }
}
