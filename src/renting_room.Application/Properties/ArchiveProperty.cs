using FluentValidation;
using Mediator;
using Microsoft.EntityFrameworkCore;
using renting_room.Application.Common.Interfaces;
using renting_room.Domain.Common;
using renting_room.Domain.Contracts;
using renting_room.Domain.Properties;

namespace renting_room.Application.Properties;

public sealed record ArchivePropertyCommand(Guid Id) : IRequest<Result>;

/// <summary>PR-BR-05: chỉ khi khu không còn hợp đồng nháp/hiệu lực/thanh lý; các phòng được ngừng dùng cùng lúc.</summary>
public sealed class ArchivePropertyHandler(IAppDbContext db, TimeProvider clock) : IRequestHandler<ArchivePropertyCommand, Result>
{
    public async ValueTask<Result> Handle(ArchivePropertyCommand request, CancellationToken cancellationToken)
    {
        await using var transaction = await db.BeginTransactionAsync(cancellationToken);
        await db.LockForUpdateAsync<Property>(request.Id, cancellationToken);

        var property = await db.Properties.FirstOrDefaultAsync(p => p.Id == request.Id, cancellationToken);
        if (property is null)
            return Result.Failure(PropertyErrors.PropertyNotFound);

        // Kể cả HĐ đã kết thúc mà hôm nay là ngày trả phòng (người thuê còn ở hết ngày).
        var today = clock.GetUtcNow().ToBusinessDate();
        var hasOpenContracts = await db.Contracts.AnyAsync(c => c.PropertyId == property.Id
            && (c.Status == ContractStatus.Draft || c.Status == ContractStatus.Active || c.Status == ContractStatus.Liquidating
                || (c.Status == ContractStatus.Ended && c.ActualEndDate >= today)),
            cancellationToken);
        if (hasOpenContracts)
            return Result.Failure(PropertyErrors.PropertyHasActiveContracts);

        var now = clock.GetUtcNow();
        var archived = property.Archive(now);
        if (archived.IsFailure)
            return archived;

        var rooms = await db.Rooms.Where(r => r.PropertyId == property.Id && r.ArchivedAt == null).ToListAsync(cancellationToken);
        rooms.ForEach(r => r.Archive(now));

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return Result.Success();
    }
}
