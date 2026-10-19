using Microsoft.EntityFrameworkCore;
using renting_room.Application.Renters;
using renting_room.Domain.Renters;

namespace renting_room.Infrastructure.Persistence.Repositories;

internal sealed class RenterRepository(AppDbContext db) : IRenterRepository
{
    public Task<Renter?> GetForUpdateAsync(Guid id, CancellationToken cancellationToken) =>
        db.Renters.FirstOrDefaultAsync(r => r.Id == id, cancellationToken);

    public Task<Guid?> FindIdByIdNumberHashAsync(string hash, Guid? excludeId, CancellationToken cancellationToken) =>
        db.Renters.Where(r => r.IdNumberHash == hash && r.Id != excludeId).Select(r => (Guid?)r.Id).FirstOrDefaultAsync(cancellationToken);

    public void Add(Renter renter) => db.Renters.Add(renter);
}

internal sealed class RenterConsentRepository(AppDbContext db) : IRenterConsentRepository
{
    public Task<RenterConsent?> GetForUpdateAsync(Guid consentId, Guid renterId, CancellationToken cancellationToken) =>
        db.RenterConsents.FirstOrDefaultAsync(c => c.Id == consentId && c.RenterId == renterId, cancellationToken);

    public void Add(RenterConsent consent) => db.RenterConsents.Add(consent);
}
