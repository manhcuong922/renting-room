using Microsoft.EntityFrameworkCore;
using renting_room.Application.Common.Models;
using renting_room.Application.Rooms;
using renting_room.Domain.Common;
using renting_room.Domain.Contracts;
using renting_room.Domain.Properties;

namespace renting_room.Infrastructure.Persistence.Queries;

internal sealed class RoomQueries(AppDbContext db) : IRoomQueries
{
    public async Task<RoomDto?> GetAsync(Guid id, DateOnly today, CancellationToken cancellationToken)
    {
        var row = await db.Rooms.AsNoTracking()
            .Where(r => r.Id == id)
            .ToRows(db, today)
            .FirstOrDefaultAsync(cancellationToken);
        return row is null ? null : (await new[] { row }.ToDtosAsync(db, today, cancellationToken))[0];
    }

    public async Task<PagedResult<RoomDto>> ListAsync(ListRoomsQuery request, DateOnly today, CancellationToken cancellationToken)
    {
        var rooms = db.Rooms.AsNoTracking();
        if (request.PropertyId is { } propertyId)
            rooms = rooms.Where(r => r.PropertyId == propertyId);
        if (request.Status != RoomDisplayStatus.Archived)
            rooms = rooms.Where(r => r.ArchivedAt == null);
        if (!string.IsNullOrWhiteSpace(request.Floor))
            rooms = rooms.Where(r => r.Floor == request.Floor.Trim());
        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var term = TextNormalizer.NormalizeCode(request.Search);
            rooms = rooms.Where(r => r.Code.Contains(term));
        }
        if (request.GroupId is { } groupId)
            rooms = rooms.Where(r => db.RoomGroups.Any(g => g.Id == groupId && g.Members.Any(m => m.RoomId == r.Id)));

        var query = rooms.ToRows(db, today);
        if (request.Status is { } status)
            query = query.Where(r => r.Status == status);
        if (request.Overdue is { } overdue)
            query = query.Where(r => (r.OverdueAmount > 0) == overdue);

        var total = await query.CountAsync(cancellationToken);
        var rows = await query
            .OrderBy(r => r.PropertyCode).ThenBy(r => r.Room.Code)
            .Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize)
            .ToListAsync(cancellationToken);

        return new PagedResult<RoomDto>(await rows.ToDtosAsync(db, today, cancellationToken), request.Page, request.PageSize, total);
    }

    public Task<bool> ExistsAsync(Guid id, CancellationToken cancellationToken) =>
        db.Rooms.AnyAsync(r => r.Id == id, cancellationToken);

    public Task<bool> HasOpenContractAsync(Guid roomId, bool includeDraft, DateOnly today, CancellationToken cancellationToken) =>
        db.Contracts.AnyAsync(c => c.RoomId == roomId
            && (c.Status == ContractStatus.Active || c.Status == ContractStatus.Liquidating
                || (c.Status == ContractStatus.Ended && c.ActualEndDate >= today)
                || (includeDraft && c.Status == ContractStatus.Draft)), cancellationToken);

    public Task<int> CountInPropertyAsync(IReadOnlyCollection<Guid> roomIds, Guid propertyId, CancellationToken cancellationToken) =>
        db.Rooms.CountAsync(r => roomIds.Contains(r.Id) && r.PropertyId == propertyId, cancellationToken);

    public async Task<IReadOnlyDictionary<Guid, string>> GetCodesAsync(IReadOnlyCollection<Guid> roomIds, CancellationToken cancellationToken) =>
        await db.Rooms.AsNoTracking().Where(r => roomIds.Contains(r.Id)).ToDictionaryAsync(r => r.Id, r => r.Code, cancellationToken);

    public Task<string?> GetCodeAsync(Guid roomId, CancellationToken cancellationToken) =>
        db.Rooms.Where(r => r.Id == roomId).Select(r => r.Code).FirstOrDefaultAsync(cancellationToken);

    public Task<int> CountAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken) =>
        db.Rooms.CountAsync(r => ids.Contains(r.Id), cancellationToken);

    public Task<int> CountGroupsAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken) =>
        db.RoomGroups.CountAsync(g => ids.Contains(g.Id), cancellationToken);

    public async Task<HashSet<string>> GetCodeSetAsync(Guid propertyId, CancellationToken cancellationToken) =>
        (await db.Rooms.Where(r => r.PropertyId == propertyId).Select(r => r.Code).ToListAsync(cancellationToken)).ToHashSet();

    public async Task<IReadOnlyDictionary<string, RoomCodeState>> ListByCodesAsync(
        Guid propertyId, IReadOnlyCollection<string> codes, CancellationToken cancellationToken) =>
        await db.Rooms.AsNoTracking().Where(r => r.PropertyId == propertyId && codes.Contains(r.Code))
            .Select(r => new RoomCodeState(r.Id, r.Code, r.ArchivedAt)).ToDictionaryAsync(r => r.Code, cancellationToken);

    public Task<Room?> FindAsync(Guid roomId, CancellationToken cancellationToken) =>
        db.Rooms.AsNoTracking().FirstOrDefaultAsync(r => r.Id == roomId, cancellationToken);

    public Task<Guid?> GetPropertyIdAsync(Guid roomId, CancellationToken cancellationToken) =>
        db.Rooms.AsNoTracking().Where(r => r.Id == roomId).Select(r => (Guid?)r.PropertyId).FirstOrDefaultAsync(cancellationToken);

    public async Task<IReadOnlyDictionary<Guid, string>> GetCodesInPropertyAsync(
        Guid propertyId, IReadOnlyCollection<Guid>? roomIds, string? floor, CancellationToken cancellationToken)
    {
        var rooms = db.Rooms.Where(r => r.PropertyId == propertyId);
        if (roomIds is { Count: > 0 })
            rooms = rooms.Where(r => roomIds.Contains(r.Id));
        if (!string.IsNullOrWhiteSpace(floor))
            rooms = rooms.Where(r => r.Floor == floor.Trim());
        return await rooms.ToDictionaryAsync(r => r.Id, r => r.Code, cancellationToken);
    }

    public async Task<IReadOnlyList<RoomCodeRow>> ListCodesAsync(
        IReadOnlyCollection<Guid> roomIds, IReadOnlyCollection<Guid> excludeIds, CancellationToken cancellationToken) =>
        await db.Rooms.Where(r => roomIds.Contains(r.Id) && !excludeIds.Contains(r.Id))
            .Select(r => new RoomCodeRow(r.Id, r.Code)).ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<Room>> ListByPropertyAsync(Guid propertyId, string? floor, CancellationToken cancellationToken) =>
        await db.Rooms.AsNoTracking().Where(r => r.PropertyId == propertyId
                && (string.IsNullOrWhiteSpace(floor) || r.Floor == floor.Trim()))
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyDictionary<Guid, Room>> ListInPropertyAsync(
        Guid propertyId, IReadOnlyCollection<Guid> roomIds, CancellationToken cancellationToken) =>
        await db.Rooms.AsNoTracking().Where(r => roomIds.Contains(r.Id) && r.PropertyId == propertyId)
            .ToDictionaryAsync(r => r.Id, cancellationToken);

    public async Task<IReadOnlyDictionary<Guid, Room>> ListAsync(IReadOnlyCollection<Guid> roomIds, CancellationToken cancellationToken) =>
        await db.Rooms.AsNoTracking().Where(r => roomIds.Contains(r.Id)).ToDictionaryAsync(r => r.Id, cancellationToken);

    public async Task<IReadOnlyDictionary<Guid, RoomPropertyCode>> GetCodesWithPropertyAsync(
        IReadOnlyCollection<Guid> roomIds, CancellationToken cancellationToken) =>
        await db.Rooms.AsNoTracking().Where(r => roomIds.Contains(r.Id))
            .Join(db.Properties, r => r.PropertyId, p => p.Id, (r, p) => new { r.Id, Room = r.Code, Property = p.Code })
            .ToDictionaryAsync(x => x.Id, x => new RoomPropertyCode(x.Room, x.Property), cancellationToken);

    public async Task<IReadOnlyList<RoomGroupDto>> ListGroupsAsync(Guid propertyId, CancellationToken cancellationToken) =>
        await db.RoomGroups.AsNoTracking()
            .Where(g => g.PropertyId == propertyId)
            .OrderBy(g => g.Name)
            .Select(g => new RoomGroupDto(g.Id, g.PropertyId, g.Name, g.Description, g.Members.Select(m => m.RoomId).ToList()))
            .ToListAsync(cancellationToken);
}
