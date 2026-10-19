using Microsoft.EntityFrameworkCore;
using renting_room.Application.Contracts;
using renting_room.Domain.Contracts;

namespace renting_room.Infrastructure.Persistence.Repositories;

internal sealed class ContractTemplateQueries(AppDbContext db) : IContractTemplateQueries
{
    public Task<ContractTemplate?> GetAsync(Guid id, CancellationToken cancellationToken) =>
        db.ContractTemplates.AsNoTracking().FirstOrDefaultAsync(t => t.Id == id, cancellationToken);

    public async Task<IReadOnlyList<ContractTemplate>> ListAsync(ContractType? type, bool includeArchived, CancellationToken cancellationToken)
    {
        var query = db.ContractTemplates.AsNoTracking();
        if (type is { } t)
            query = query.Where(x => x.ContractType == t);
        if (!includeArchived)
            query = query.Where(x => x.ArchivedAt == null);
        return await query.OrderBy(x => x.ContractType).ThenBy(x => x.Name).ToListAsync(cancellationToken);
    }
}
