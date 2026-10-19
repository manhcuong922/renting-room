using renting_room.Domain.Renters;

namespace renting_room.Application.Renters;

public interface IRenterConsentRepository
{
    Task<RenterConsent?> GetForUpdateAsync(Guid consentId, Guid renterId, CancellationToken cancellationToken);

    void Add(RenterConsent consent);
}
