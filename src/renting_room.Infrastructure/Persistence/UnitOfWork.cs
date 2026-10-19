using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using renting_room.Application.Common.Interfaces;
using renting_room.Domain.Common;

namespace renting_room.Infrastructure.Persistence;

internal sealed class UnitOfWork(AppDbContext db) : IUnitOfWork
{
    public async Task<ITransaction> BeginTransactionAsync(CancellationToken cancellationToken) =>
        new Transaction(await db.BeginTransactionAsync(cancellationToken));

    public async Task<ITransaction> BeginTransactionAsync(IsolationLevel isolationLevel, CancellationToken cancellationToken) =>
        new Transaction(await db.Database.BeginTransactionAsync(isolationLevel, cancellationToken));

    public Task SaveChangesAsync(CancellationToken cancellationToken) => db.SaveChangesAsync(cancellationToken);

    public bool IsSaveFailure(Exception exception) => exception is DbUpdateException;

    public async Task<SaveOutcome> TrySaveChangesAsync(CancellationToken cancellationToken)
    {
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            return SaveOutcome.Saved;
        }
        catch (DbUpdateConcurrencyException)
        {
            return SaveOutcome.Conflict;
        }
        catch (DbUpdateException)
        {
            return SaveOutcome.Failed;
        }
    }

    public void SetExpectedVersion(AuditableEntity entity, uint version) => db.SetExpectedVersion(entity, version);

    public Task LockForUpdateAsync<TEntity>(Guid id, CancellationToken cancellationToken) where TEntity : Entity =>
        db.LockForUpdateAsync<TEntity>(id, cancellationToken);

    public Task LockRentersAsync(IEnumerable<Guid> renterIds, CancellationToken cancellationToken) =>
        db.LockRentersAsync(renterIds, cancellationToken);

    public void ClearTracking() => db.ChangeTracker.Clear();

    private sealed class Transaction(IDbContextTransaction inner) : ITransaction
    {
        public Task CommitAsync(CancellationToken cancellationToken) => inner.CommitAsync(cancellationToken);

        public Task RollbackAsync(CancellationToken cancellationToken) => inner.RollbackAsync(cancellationToken);

        public ValueTask DisposeAsync() => inner.DisposeAsync();
    }
}
