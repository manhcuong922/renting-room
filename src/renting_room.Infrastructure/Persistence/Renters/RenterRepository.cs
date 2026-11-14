using Microsoft.EntityFrameworkCore;
using renting_room.Application.Common.Interfaces;
using renting_room.Application.Renters;
using renting_room.Domain.Renters;

namespace renting_room.Infrastructure.Persistence.Repositories;

internal sealed class RenterRepository(AppDbContext db, ICurrentUser currentUser) : IRenterRepository
{
    public Task<Renter?> GetForUpdateAsync(Guid id, CancellationToken cancellationToken) =>
        db.Renters.FirstOrDefaultAsync(r => r.Id == id, cancellationToken);

    public Task<Guid?> FindIdByIdNumberHashAsync(string hash, Guid? excludeId, CancellationToken cancellationToken) =>
        OrganizationRenters().Where(r => r.IdNumberHash == hash && r.Id != excludeId).Select(r => (Guid?)r.Id).FirstOrDefaultAsync(cancellationToken);

    public async Task<(string FullName, string? IdNumberLast4)> ClaimAsync(Guid renterId, CancellationToken cancellationToken)
    {
        if (currentUser.PropertyIds is not null && currentUser.UserId is { } userId
            && !await db.Renters.AnyAsync(r => r.Id == renterId, cancellationToken)
            && !db.RenterClaims.Local.Any(c => c.RenterId == renterId && c.UserId == userId))
            db.RenterClaims.Add(RenterClaim.Create(renterId, userId));
        var renter = await OrganizationRenters().Where(r => r.Id == renterId).Select(r => new { r.FullName, r.IdNumberLast4 }).FirstAsync(cancellationToken);
        return (renter.FullName, renter.IdNumberLast4);
    }

    /// <summary>Hồ sơ của tổ chức hiện tại, bỏ lọc phạm vi khu (lọc tổ chức viết tay vì IgnoreQueryFilters bỏ cả hai).</summary>
    private IQueryable<Renter> OrganizationRenters() =>
        db.Renters.IgnoreQueryFilters().Where(r => r.OrganizationId == db.CurrentOrganizationId);

    public void Add(Renter renter) => db.Renters.Add(renter);
}
