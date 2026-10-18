using FluentValidation;
using Mediator;
using Microsoft.EntityFrameworkCore;
using renting_room.Application.Common.Interfaces;
using renting_room.Domain.Common;
using renting_room.Domain.Contracts;
using renting_room.Domain.Fees;

namespace renting_room.Application.Fees;

public sealed record ChangeFeeTypeStateCommand(Guid Id, bool Archive) : IRequest<Result>;

/// <summary>Ngừng dùng chỉ khi không còn HĐ nháp / hiệu lực / thanh lý đang gắn khoản này (thay FE-BR-12 bản đầu).</summary>
public sealed class ChangeFeeTypeStateHandler(IAppDbContext db, TimeProvider clock) : IRequestHandler<ChangeFeeTypeStateCommand, Result>
{
    public async ValueTask<Result> Handle(ChangeFeeTypeStateCommand request, CancellationToken cancellationToken)
    {
        var fee = await db.FeeTypes.FirstOrDefaultAsync(f => f.Id == request.Id, cancellationToken);
        if (fee is null)
            return Result.Failure(FeeErrors.NotFound);

        if (request.Archive)
        {
            var today = clock.GetUtcNow().ToBusinessDate();
            var inUse = await db.Contracts
                .Where(c => c.Status == ContractStatus.Draft || c.Status == ContractStatus.Active || c.Status == ContractStatus.Liquidating)
                .SelectMany(c => c.Fees)
                .AnyAsync(f => f.FeeTypeId == fee.Id && (f.EffectiveTo == null || f.EffectiveTo >= today), cancellationToken);
            if (inUse)
                return Result.Failure(FeeErrors.InUse);
            if (fee.Group == FeeGroup.Metered && await db.Meters.AnyAsync(m => m.FeeTypeId == fee.Id && m.RemovedDate == null, cancellationToken))
                return Result.Failure(FeeErrors.HasActiveMeters);
            var archived = fee.Archive(clock.GetUtcNow());
            if (archived.IsFailure)
                return archived;
        }
        else
        {
            if (await db.FeeTypes.AnyAsync(f => f.PropertyId == fee.PropertyId && f.ArchivedAt == null
                    && (f.NameNormalized == fee.NameNormalized || (fee.SystemCode != null && f.SystemCode == fee.SystemCode)), cancellationToken))
                return Result.Failure(FeeErrors.NameTaken);
            var restored = fee.Restore();
            if (restored.IsFailure)
                return restored;
        }

        await db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
