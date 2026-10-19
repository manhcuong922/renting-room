using Microsoft.EntityFrameworkCore;
using renting_room.Application.Common.Models;
using renting_room.Application.Rooms;
using renting_room.Domain.Billing;
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
        // PR-BR-16: bộ lọc "Quá hạn" — phòng có phiếu quá hạn thanh toán chưa thu đủ.
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

/// <summary>
/// PR-BR-02: trạng thái phòng dẫn xuất theo ngày D — không thể lệch với hợp đồng.
/// Thứ tự ưu tiên: Ngừng dùng → Bảo trì → Đang thuê → Giữ chỗ (có HĐ nháp) → Trống.
/// </summary>
internal sealed class RoomRow
{
    public Room Room { get; init; } = null!;
    public string PropertyCode { get; init; } = null!;
    public CurrentContractDto? Current { get; init; }
    public RoomDisplayStatus Status { get; init; }

    /// <summary>PR-BR-16 / PM-UC-12: tổng phiếu đã chốt chưa thu đủ của các HĐ đang gắn với phòng ⇒ nhãn "Còn nợ".</summary>
    public decimal OutstandingAmount { get; init; }

    /// <summary>PR-BR-16 (K3): phần còn nợ của phiếu đã quá hạn thanh toán (ngày chốt phiếu + số ngày hạn của khu) ⇒ nhãn đỏ "Quá hạn".</summary>
    public decimal OverdueAmount { get; init; }

    /// <summary>M08 PM-BR-32: cọc HĐ hiện tại của phòng đang giữ (chưa hoàn trả / chưa chuyển) — 0 = không cọc / đã hoàn.</summary>
    public decimal DepositHeld { get; init; }

    public RoomDto ToDto(IReadOnlyDictionary<Guid, IReadOnlyList<ContractFlag>> flags) => new(
        Room.Id, Room.PropertyId, PropertyCode, Room.Code, Room.Floor, Room.AreaM2, Room.MaxOccupants, Room.ListedRent,
        Room.DefaultDeposit, Room.Amenities, Room.Description, Status, Room.MaintenanceNote,
        // PR-BR-16: nhãn về thời hạn hợp đồng chỉ hiện ở hợp đồng, không đưa lên thẻ phòng.
        Current is null
            ? null
            : Current with { Flags = flags.GetValueOrDefault(Current.Id, []).Where(f => f != ContractFlag.ExpiredAwaitingDecision).ToList() },
        Room.Version.ToString(), OutstandingAmount,
        OverdueAmount, DepositHeld);
}

internal static class RoomStatusQuery
{
    /// <summary>Dùng object initializer (không dùng constructor) để EF dịch được Where/OrderBy trên Status.</summary>
    public static IQueryable<RoomRow> ToRows(this IQueryable<Room> rooms, AppDbContext db, DateOnly today) =>
        rooms.Select(r => new
        {
            Room = r,
            PropertyCode = db.Properties.Where(p => p.Id == r.PropertyId).Select(p => p.Code).First(),
            // PR-BR-02 + CT-BR-14: HĐ đang ở phòng từ ngày vào phòng, hoặc HĐ đã chuyển khỏi phòng nhưng hôm nay là ngày chuyển (còn ở).
            Current = db.Contracts
                .Where(c => (c.Status == ContractStatus.Active || c.Status == ContractStatus.Liquidating || c.Status == ContractStatus.Ended)
                    && ((c.RoomId == r.Id && c.RoomSince <= today && (c.ActualEndDate == null || c.ActualEndDate >= today))
                        || c.RoomMoves.Any(m => m.RoomId == r.Id && m.FromDate <= today && m.ToDate >= today)))
                .OrderBy(c => c.RoomId == r.Id ? 0 : 1)
                .Select(c => new CurrentContractDto(
                    c.Id,
                    c.ContractNo,
                    db.Renters.Where(x => x.Id == c.RepresentativeRenterId).Select(x => x.FullName).First(),
                    c.StartDate,
                    c.EndDate,
                    c.Occupants.Count(o => o.MoveInDate <= today && (o.MoveOutDate == null || o.MoveOutDate >= today)),
                    null))
                .FirstOrDefault(),
            // PR-BR-16: nợ đi theo HĐ — phiếu của các HĐ đang gắn với phòng (chuyển phòng ⇒ nợ sang phòng mới), không theo phòng ghi trên phiếu.
            Outstanding = db.Invoices.Where(i => db.Contracts.Any(c => c.Id == i.ContractId && c.RoomId == r.Id)
                    && i.Status == InvoiceStatus.Finalized && i.PaidAmount < i.TotalAmount)
                .Sum(i => (decimal?)(i.TotalAmount - i.PaidAmount)) ?? 0,
            Overdue = db.Invoices.Where(i => db.Contracts.Any(c => c.Id == i.ContractId && c.RoomId == r.Id)
                    && i.Status == InvoiceStatus.Finalized && i.PaidAmount < i.TotalAmount
                    && i.DueDate < today)
                .Sum(i => (decimal?)(i.TotalAmount - i.PaidAmount)) ?? 0,
            DepositHeld = db.Contracts
                .Where(c => c.RoomId == r.Id && (c.Status == ContractStatus.Active || c.Status == ContractStatus.Liquidating)
                    && c.DepositStatus == DepositStatus.Holding)
                .Sum(c => (decimal?)c.DepositAmount) ?? 0,
            // Giữ chỗ: có hợp đồng nháp, hoặc đã kích hoạt nhưng ngày bàn giao ở tương lai (kích hoạt trước 1 ngày).
            HasDraft = db.Contracts.Any(c => c.RoomId == r.Id
                && (c.Status == ContractStatus.Draft || (c.Status == ContractStatus.Active && c.StartDate > today)))
        })
        .Select(x => new RoomRow
        {
            Room = x.Room,
            PropertyCode = x.PropertyCode,
            Current = x.Current,
            OutstandingAmount = x.Outstanding,
            OverdueAmount = x.Overdue,
            DepositHeld = x.DepositHeld,
            Status = x.Room.ArchivedAt != null ? RoomDisplayStatus.Archived
                : x.Room.IsUnderMaintenance ? RoomDisplayStatus.Maintenance
                : x.Current != null ? RoomDisplayStatus.Occupied
                : x.HasDraft ? RoomDisplayStatus.Reserved
                : RoomDisplayStatus.Vacant
        });

    public static async Task<IReadOnlyList<RoomDto>> ToDtosAsync(
        this IReadOnlyList<RoomRow> rows, AppDbContext db, DateOnly today, CancellationToken ct)
    {
        var flags = await ContractQueries.LoadFlagsAsync(db, rows.Where(r => r.Current is not null).Select(r => r.Current!.Id), today, ct);
        return rows.Select(r => r.ToDto(flags)).ToList();
    }

    /// <summary>
    /// PR-BR-02: phòng có HĐ đã bàn giao chiếm tại ngày <paramref name="date"/> — phòng hiện tại của HĐ (ngày trả phòng vẫn tính — CT-BR-33),
    /// hoặc phòng HĐ đã chuyển đi mà còn tính tới ngày chuyển (CT-BR-14).
    /// </summary>
    public static IQueryable<Guid> OccupiedRoomIds(this AppDbContext db, DateOnly date) =>
        db.Contracts
            .Where(c => (c.Status == ContractStatus.Active || c.Status == ContractStatus.Liquidating || c.Status == ContractStatus.Ended)
                && c.RoomSince <= date && (c.ActualEndDate == null || c.ActualEndDate >= date))
            .Select(c => c.RoomId)
            .Union(db.Contracts.SelectMany(c => c.RoomMoves).Where(m => m.FromDate <= date && m.ToDate >= date).Select(m => m.RoomId));
}
