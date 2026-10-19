using Microsoft.EntityFrameworkCore;
using renting_room.Application.Common.Models;
using renting_room.Application.Renters;
using renting_room.Domain.Billing;
using renting_room.Domain.Contracts;
using renting_room.Domain.Renters;

namespace renting_room.Infrastructure.Persistence.Queries;

internal sealed class RenterQueries(AppDbContext db) : IRenterQueries
{
    private static readonly ContractStatus[] OpenStatuses = [ContractStatus.Draft, ContractStatus.Active, ContractStatus.Liquidating];

    public Task<RenterDto?> GetAsync(Guid id, CancellationToken cancellationToken) =>
        db.Renters.AsNoTracking().Where(r => r.Id == id).ToDto().FirstOrDefaultAsync(cancellationToken);

    public Task<bool> ExistsAsync(Guid id, CancellationToken cancellationToken) =>
        db.Renters.AnyAsync(r => r.Id == id, cancellationToken);

    public async Task<bool?> IsAnonymizedAsync(Guid id, CancellationToken cancellationToken) =>
        await db.Renters.AsNoTracking().Where(r => r.Id == id).Select(r => (bool?)(r.AnonymizedAt != null)).FirstOrDefaultAsync(cancellationToken);

    public Task<RenterIdNumber?> GetIdNumberAsync(Guid id, CancellationToken cancellationToken) =>
        db.Renters.AsNoTracking().Where(r => r.Id == id)
            .Select(r => new RenterIdNumber(r.IdNumberEncrypted, r.AnonymizedAt != null)).FirstOrDefaultAsync(cancellationToken);

    public async Task<PagedResult<RenterDto>> SearchAsync(RenterSearch search, CancellationToken cancellationToken)
    {
        var query = db.Renters.AsNoTracking();
        if (!search.IncludeArchived)
            query = query.Where(r => r.ArchivedAt == null);
        if (search.PropertyId is { } propertyId)
            query = query.Where(r => db.Contracts.Any(c => c.PropertyId == propertyId && c.Status != ContractStatus.Cancelled
                && (c.RepresentativeRenterId == r.Id || c.Occupants.Any(o => o.RenterId == r.Id))));
        if (search.Staying is { } staying)
        {
            var today = search.Today;
            query = query.Where(r => db.Contracts.Any(c => (c.Status == ContractStatus.Active || c.Status == ContractStatus.Liquidating)
                && c.Occupants.Any(o => o.RenterId == r.Id && o.MoveInDate <= today && (o.MoveOutDate == null || o.MoveOutDate >= today))) == staying);
        }

        if (search.IdNumberHashes is { } hashes)
            query = query.Where(r => hashes.Contains(r.IdNumberHash));

        if (search.Text is { } text)
        {
            var phone = search.Phone;
            query = query.Where(r => r.FullNameSearch.Contains(text) || (phone != null && r.Phone == phone));
        }

        var total = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderBy(r => r.FullNameSearch)
            .Skip((search.Page - 1) * search.PageSize)
            .Take(search.PageSize)
            .ToDto()
            .ToListAsync(cancellationToken);

        return new PagedResult<RenterDto>(items, search.Page, search.PageSize, total);
    }

    public async Task<IReadOnlyList<RenterConsent>> ListConsentsAsync(Guid renterId, CancellationToken cancellationToken) =>
        await db.RenterConsents.AsNoTracking().Where(c => c.RenterId == renterId)
            .OrderByDescending(c => c.GivenAt).ThenByDescending(c => c.CreatedAt).ToListAsync(cancellationToken);

    public async Task<IReadOnlyDictionary<Guid, string>> GetNamesAsync(IReadOnlyCollection<Guid> renterIds, CancellationToken cancellationToken) =>
        await db.Renters.AsNoTracking().Where(r => renterIds.Contains(r.Id)).ToDictionaryAsync(r => r.Id, r => r.FullName, cancellationToken);

    public async Task<IReadOnlyDictionary<Guid, string>> SearchNamesAsync(string searchText, int max, CancellationToken cancellationToken) =>
        await db.Renters.AsNoTracking().Where(r => r.FullNameSearch.Contains(searchText)).OrderBy(r => r.FullNameSearch).Take(max)
            .ToDictionaryAsync(r => r.Id, r => r.FullName, cancellationToken);

    public async Task<IReadOnlyDictionary<Guid, Renter>> ListAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken) =>
        await db.Renters.AsNoTracking().Where(r => ids.Contains(r.Id)).ToDictionaryAsync(r => r.Id, cancellationToken);

    public Task<Renter?> FindActiveAsync(Guid id, CancellationToken cancellationToken) =>
        db.Renters.AsNoTracking().FirstOrDefaultAsync(r => r.Id == id && r.AnonymizedAt == null, cancellationToken);

    public async Task<IReadOnlyList<RenterPerson>> ListPeopleAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken) =>
        await db.Renters.AsNoTracking().Where(r => ids.Contains(r.Id) && r.AnonymizedAt == null)
            .Select(r => new RenterPerson(r.Id, r.DateOfBirth, r.Gender, r.FullName))
            .ToListAsync(cancellationToken);

    public Task<bool> HasIdNumberAsync(Guid id, CancellationToken cancellationToken) =>
        db.Renters.AnyAsync(r => r.Id == id && r.IdNumberHash != null, cancellationToken);

    public Task<int> CountWithIdNumberAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken) =>
        db.Renters.CountAsync(r => ids.Contains(r.Id) && r.IdNumberHash != null, cancellationToken);

    public async Task<IReadOnlyList<RenterPerson>> ListMissingIdNumberAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken) =>
        await db.Renters.AsNoTracking()
            .Where(r => ids.Contains(r.Id) && r.IdNumberHash == null && r.AnonymizedAt == null)
            .Select(r => new RenterPerson(r.Id, r.DateOfBirth, r.Gender, r.FullName)).ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<RenterIdentity>> ListByIdNumberHashesAsync(IReadOnlyCollection<string?> hashes, CancellationToken cancellationToken) =>
        await db.Renters.AsNoTracking().Where(r => hashes.Contains(r.IdNumberHash))
            .Select(r => new RenterIdentity(r.Id, r.IdNumberHash, r.FullName, r.DateOfBirth, r.Phone)).ToListAsync(cancellationToken);

    public Task<RenterContact?> GetContactAsync(Guid id, CancellationToken cancellationToken) =>
        db.Renters.AsNoTracking().Where(r => r.Id == id)
            .Select(r => new RenterContact(r.Phone, r.DateOfBirth, r.AnonymizedAt != null)).FirstOrDefaultAsync(cancellationToken);

    public Task<bool> HasOpenContractAsync(Guid renterId, CancellationToken cancellationToken) =>
        db.Contracts.AnyAsync(c => (c.RepresentativeRenterId == renterId || c.Occupants.Any(o => o.RenterId == renterId)
                || c.RepresentativeHistory.Any(h => h.RenterId == renterId))
            && OpenStatuses.Contains(c.Status), cancellationToken);

    public Task<bool> HasUnsettledInvoiceAsync(Guid renterId, CancellationToken cancellationToken) =>
        db.Invoices.AnyAsync(i => i.Status == InvoiceStatus.Finalized
            && db.Contracts.Any(c => c.Id == i.ContractId && c.RepresentativeRenterId == renterId)
            && (i.PaidAmount < i.TotalAmount || (i.TotalAmount < 0 && i.RefundedOn == null)), cancellationToken);

    public async Task<IReadOnlyList<AnonymizationCandidate>> ListAnonymizationCandidatesAsync(CancellationToken cancellationToken) =>
        await WithoutBlockers(db.Renters.AsNoTracking().Where(r => r.AnonymizedAt == null))
            .Select(r => new AnonymizationCandidate(
                r.Id,
                r.CreatedAt,
                db.Contracts
                    .Where(c => (c.RepresentativeRenterId == r.Id || c.Occupants.Any(o => o.RenterId == r.Id) || c.RepresentativeHistory.Any(h => h.RenterId == r.Id)) && c.Status == ContractStatus.Ended)
                    .Max(c => c.ActualEndDate),
                db.Contracts
                    .Where(c => (c.RepresentativeRenterId == r.Id || c.Occupants.Any(o => o.RenterId == r.Id) || c.RepresentativeHistory.Any(h => h.RenterId == r.Id)) && c.Status == ContractStatus.Cancelled)
                    .Max(c => c.CancelledAt)))
            .ToListAsync(cancellationToken);

    /// <summary>Lọc bỏ hồ sơ còn HĐ đang chạy hoặc còn phiếu nợ / chờ hoàn (cùng điều kiện với 2 hàm trên, dạng truy vấn tập hợp).</summary>
    private IQueryable<Renter> WithoutBlockers(IQueryable<Renter> renters) => renters
        .Where(r => !db.Contracts.Any(c => (c.RepresentativeRenterId == r.Id || c.Occupants.Any(o => o.RenterId == r.Id) || c.RepresentativeHistory.Any(h => h.RenterId == r.Id))
            && OpenStatuses.Contains(c.Status)))
        .Where(r => !db.Invoices.Any(i => i.Status == InvoiceStatus.Finalized
            && db.Contracts.Any(c => c.Id == i.ContractId && c.RepresentativeRenterId == r.Id)
            && (i.PaidAmount < i.TotalAmount || (i.TotalAmount < 0 && i.RefundedOn == null))));
}

internal static class RenterDtoProjection
{
    public static IQueryable<RenterDto> ToDto(this IQueryable<Renter> renters) =>
        renters.Select(r => new RenterDto(
            r.Id, r.FullName, r.DateOfBirth, r.Gender, r.Phone, r.Email, r.Nationality, r.IdType,
            r.IdNumberLast4 == null ? null : "********" + r.IdNumberLast4,
            r.IdIssueDate, r.IdIssuePlace, r.PermanentAddress, r.Occupation, r.Workplace,
            r.EmergencyContactName, r.EmergencyContactPhone, r.Note, r.CreatedAt, r.Version.ToString(), r.AnonymizedAt, r.ArchivedAt));
}
