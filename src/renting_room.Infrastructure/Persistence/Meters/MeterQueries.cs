using Microsoft.EntityFrameworkCore;
using renting_room.Application.Meters;
using renting_room.Domain.Meters;

namespace renting_room.Infrastructure.Persistence.Queries;

internal sealed class MeterQueries(AppDbContext db) : IMeterQueries
{
    public Task<Meter?> GetAsync(Guid id, CancellationToken cancellationToken) =>
        db.Meters.AsNoTracking().Include(m => m.Readings).FirstOrDefaultAsync(m => m.Id == id, cancellationToken);

    public async Task<IReadOnlyList<Meter>> ListAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken) =>
        await db.Meters.AsNoTracking().Include(m => m.Readings).Where(m => ids.Contains(m.Id)).ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<Meter>> ListByRoomAsync(Guid roomId, bool includeRemoved, CancellationToken cancellationToken) =>
        await db.Meters.AsNoTracking().Include(m => m.Readings)
            .Where(m => m.RoomId == roomId && (includeRemoved || m.RemovedDate == null))
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<Meter>> ListByRoomsAsync(IReadOnlyCollection<Guid> roomIds, CancellationToken cancellationToken) =>
        await db.Meters.AsNoTracking().Include(m => m.Readings).Where(m => roomIds.Contains(m.RoomId)).ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<Meter>> ListByPropertyAsync(Guid propertyId, CancellationToken cancellationToken) =>
        await db.Meters.AsNoTracking().Include(m => m.Readings).Where(m => m.PropertyId == propertyId).ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<Meter>> ListActiveByFeeAsync(Guid propertyId, Guid feeTypeId, CancellationToken cancellationToken) =>
        await db.Meters.AsNoTracking().Include(m => m.Readings)
            .Where(m => m.PropertyId == propertyId && m.FeeTypeId == feeTypeId && m.RemovedDate == null)
            .ToListAsync(cancellationToken);

    public Task<List<Guid>> ListActiveIdsInRoomAsync(Guid roomId, DateOnly date, CancellationToken cancellationToken) =>
        db.Meters.Where(m => m.RoomId == roomId && m.InstalledDate <= date && (m.RemovedDate == null || m.RemovedDate > date))
            .Select(m => m.Id).ToListAsync(cancellationToken);

    public Task<List<Guid>> ListIdsInRoomAsync(Guid roomId, CancellationToken cancellationToken) =>
        db.Meters.Where(m => m.RoomId == roomId).Select(m => m.Id).ToListAsync(cancellationToken);

    public async Task<HashSet<Guid>> GetMeteredRoomIdsAsync(IReadOnlyCollection<Guid> roomIds, Guid feeTypeId, CancellationToken cancellationToken) =>
        (await db.Meters.Where(m => roomIds.Contains(m.RoomId) && m.FeeTypeId == feeTypeId && m.RemovedDate == null)
            .Select(m => m.RoomId).ToListAsync(cancellationToken)).ToHashSet();

    public async Task<HashSet<Guid>> GetMeteredRoomIdsInPropertyAsync(Guid propertyId, Guid feeTypeId, CancellationToken cancellationToken) =>
        (await db.Meters.Where(m => m.PropertyId == propertyId && m.FeeTypeId == feeTypeId && m.RemovedDate == null)
            .Select(m => m.RoomId).ToListAsync(cancellationToken)).ToHashSet();

    public Task<bool> HasActiveAsync(Guid roomId, Guid feeTypeId, CancellationToken cancellationToken) =>
        db.Meters.AnyAsync(m => m.RoomId == roomId && m.FeeTypeId == feeTypeId && m.RemovedDate == null, cancellationToken);

    public Task<List<Guid>> ListFeeIdsInRoomAsync(Guid roomId, DateOnly from, CancellationToken cancellationToken) =>
        db.Meters.Where(m => m.RoomId == roomId && (m.RemovedDate == null || m.RemovedDate > from))
            .Select(m => m.FeeTypeId).Distinct().ToListAsync(cancellationToken);

    public Task<Guid?> GetMeterIdOfReadingAsync(Guid readingId, CancellationToken cancellationToken) =>
        db.Meters.SelectMany(m => m.Readings).Where(r => r.Id == readingId)
            .Select(r => (Guid?)r.MeterId).FirstOrDefaultAsync(cancellationToken);

    public async Task<IReadOnlyList<MeterRoomRow>> ListRoomMetersAsync(IReadOnlyCollection<Guid> roomIds, CancellationToken cancellationToken) =>
        await db.Meters.Where(m => roomIds.Contains(m.RoomId)).Select(m => new MeterRoomRow(m.Id, m.RoomId)).ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<MeterFeeRow>> ListActiveFeesAsync(IReadOnlyCollection<Guid> roomIds, DateOnly date, CancellationToken cancellationToken) =>
        await db.Meters
            .Where(m => roomIds.Contains(m.RoomId) && m.InstalledDate <= date && (m.RemovedDate == null || m.RemovedDate > date))
            .Select(m => new MeterFeeRow(m.RoomId, m.FeeTypeId)).ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<MeterFeeRow>> ListUnremovedFeesAsync(IReadOnlyCollection<Guid> roomIds, CancellationToken cancellationToken) =>
        await db.Meters.AsNoTracking().Where(m => roomIds.Contains(m.RoomId) && m.RemovedDate == null)
            .Select(m => new MeterFeeRow(m.RoomId, m.FeeTypeId)).ToListAsync(cancellationToken);

    public Task<bool> HasMeterOnAsync(Guid roomId, Guid feeTypeId, DateOnly date, CancellationToken cancellationToken) =>
        db.Meters.AnyAsync(m => m.RoomId == roomId && m.FeeTypeId == feeTypeId
            && (m.RemovedDate == null || m.RemovedDate > date), cancellationToken);

    public Task<bool> AnyReadingAfterAsync(IReadOnlyCollection<Guid> readingIds, DateOnly date, CancellationToken cancellationToken) =>
        db.Meters.SelectMany(m => m.Readings).AnyAsync(r => readingIds.Contains(r.Id) && r.ReadingDate > date, cancellationToken);

    public async Task<IReadOnlyList<MeterLastReadingRow>> ListLastReadingsAsync(IReadOnlyCollection<Guid> roomIds, CancellationToken cancellationToken) =>
        await db.Meters.AsNoTracking().Where(m => roomIds.Contains(m.RoomId) && m.RemovedDate == null)
            .Select(m => new MeterLastReadingRow(m.RoomId, m.SerialNo, m.Readings.Max(r => (DateOnly?)r.ReadingDate))).ToListAsync(cancellationToken);

    public async Task<ReadingValue?> GetActiveReadingAsync(Guid readingId, CancellationToken cancellationToken)
    {
        var reading = await db.Meters.SelectMany(m => m.Readings).Where(r => r.Id == readingId && r.VoidedAt == null)
            .Select(r => new { r.MeterId, r.Value }).FirstOrDefaultAsync(cancellationToken);
        return reading is null ? null : new ReadingValue(reading.MeterId, reading.Value);
    }
}
