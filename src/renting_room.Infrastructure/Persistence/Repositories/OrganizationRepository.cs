using Microsoft.EntityFrameworkCore;
using renting_room.Application.Identity.Organizations;
using renting_room.Domain.Identity;

namespace renting_room.Infrastructure.Persistence.Repositories;

internal sealed class OrganizationRepository(AppDbContext db) : IOrganizationRepository
{
    public Task<Organization?> GetAsync(Guid id, CancellationToken cancellationToken) =>
        db.Organizations.FirstOrDefaultAsync(o => o.Id == id, cancellationToken);

    public Task<bool> IsCodeTakenAsync(string code, CancellationToken cancellationToken) =>
        db.Organizations.AnyAsync(o => o.Code == code, cancellationToken);

    public void Add(Organization organization) => db.Organizations.Add(organization);
}
