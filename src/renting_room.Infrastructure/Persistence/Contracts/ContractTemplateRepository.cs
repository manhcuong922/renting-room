using Microsoft.EntityFrameworkCore;
using renting_room.Application.Contracts;
using renting_room.Domain.Contracts;

namespace renting_room.Infrastructure.Persistence.Repositories;

internal sealed class ContractTemplateRepository(AppDbContext db) : IContractTemplateRepository
{
    public Task<ContractTemplate?> GetForUpdateAsync(Guid id, CancellationToken cancellationToken) =>
        db.ContractTemplates.FirstOrDefaultAsync(t => t.Id == id, cancellationToken);

    public Task<bool> IsNameTakenAsync(string name, Guid? excludeId, CancellationToken cancellationToken) =>
        excludeId is { } id
            ? db.ContractTemplates.AnyAsync(t => t.Name == name && t.Id != id, cancellationToken)
            : db.ContractTemplates.AnyAsync(t => t.Name == name, cancellationToken);

    public void Add(ContractTemplate template) => db.ContractTemplates.Add(template);
}
