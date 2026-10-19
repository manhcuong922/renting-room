using Microsoft.EntityFrameworkCore;
using renting_room.Application.Properties;
using renting_room.Domain.Properties;

namespace renting_room.Infrastructure.Persistence.Repositories;

internal sealed class PropertyRepository(AppDbContext db) : IPropertyRepository
{
    public Task<Property?> GetAsync(Guid id, CancellationToken cancellationToken) =>
        db.Properties.FirstOrDefaultAsync(p => p.Id == id, cancellationToken);

    public async Task<Property?> GetForUpdateAsync(Guid id, CancellationToken cancellationToken)
    {
        await db.LockForUpdateAsync<Property>(id, cancellationToken);
        return await GetAsync(id, cancellationToken);
    }

    public Task<bool> IsCodeTakenAsync(string code, CancellationToken cancellationToken) =>
        db.Properties.AnyAsync(p => p.Code == code, cancellationToken);

    public void Add(Property property) => db.Properties.Add(property);
}
