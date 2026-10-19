using Microsoft.EntityFrameworkCore;
using renting_room.Application.Renters;
using renting_room.Domain.Renters;

namespace renting_room.Infrastructure.Persistence.Repositories;

internal sealed class RenterConsentRepository(AppDbContext db) : IRenterConsentRepository
{
    public Task<RenterConsent?> GetForUpdateAsync(Guid consentId, Guid renterId, CancellationToken cancellationToken) =>
        db.RenterConsents.FirstOrDefaultAsync(c => c.Id == consentId && c.RenterId == renterId, cancellationToken);

    public void Add(RenterConsent consent) => db.RenterConsents.Add(consent);
}
