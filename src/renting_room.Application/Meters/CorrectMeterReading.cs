using FluentValidation;
using Mediator;
using Microsoft.EntityFrameworkCore;
using renting_room.Application.Common.Interfaces;
using renting_room.Application.Common.Validation;
using renting_room.Domain.Common;
using renting_room.Domain.Meters;

namespace renting_room.Application.Meters;

// Sửa chỉ số
public sealed record CorrectMeterReadingCommand(Guid ReadingId, decimal Value, string? Note) : IRequest<Result<MeterReadingDto>>;

public sealed class CorrectMeterReadingCommandValidator : AbstractValidator<CorrectMeterReadingCommand>
{
    public CorrectMeterReadingCommandValidator()
    {
        RuleFor(x => x.Value).ReadingValue();
        RuleFor(x => x.Note).OptionalText(300);
    }
}

/// <summary>MT-UC-06: sửa chỉ số nhập sai (VD chỉnh chỉ số nhận phòng 100 → 108) — vẫn đơn điệu; đã dùng cho phiếu đã chốt ⇒ khóa (MT-BR-06).</summary>
public sealed class CorrectMeterReadingHandler(IAppDbContext db) : IRequestHandler<CorrectMeterReadingCommand, Result<MeterReadingDto>>
{
    public async ValueTask<Result<MeterReadingDto>> Handle(CorrectMeterReadingCommand request, CancellationToken cancellationToken)
    {
        var meterId = await db.Meters.SelectMany(m => m.Readings).Where(r => r.Id == request.ReadingId)
            .Select(r => (Guid?)r.MeterId).FirstOrDefaultAsync(cancellationToken);
        if (meterId is null)
            return MeterErrors.ReadingNotFound;

        await using var transaction = await db.BeginTransactionAsync(cancellationToken);
        var meter = (await MeterMapping.LockAndLoadAsync(db, [meterId.Value], cancellationToken)).Single();
        if ((await ReadingLocks.LockedReadingIdsAsync(db, [meter.Id], cancellationToken)).Contains(request.ReadingId))
            return MeterErrors.ReadingLocked;
        var corrected = meter.Correct(request.ReadingId, request.Value, request.Note);
        if (corrected.IsFailure)
            return corrected.Error!;

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return corrected.Value!.ToDto();
    }
}
