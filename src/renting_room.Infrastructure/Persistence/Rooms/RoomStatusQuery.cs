using Microsoft.EntityFrameworkCore;
using renting_room.Application.Rooms;
using renting_room.Domain.Billing;
using renting_room.Domain.Contracts;
using renting_room.Domain.Properties;

namespace renting_room.Infrastructure.Persistence.Queries;

internal static class RoomStatusQuery
{
    /// <summary>Dùng object initializer (không dùng constructor) để EF dịch được Where/OrderBy trên Status.</summary>
    public static IQueryable<RoomRow> ToRows(this IQueryable<Room> rooms, AppDbContext db, DateOnly today) =>
        rooms.Select(r => new
        {
            Room = r,
            PropertyCode = db.Properties.Where(p => p.Id == r.PropertyId).Select(p => p.Code).First(),
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
                    null,
                    c.Status == ContractStatus.Liquidating ? c.ActualEndDate : c.Status == ContractStatus.Active ? c.PlannedMoveOutDate : null))
                .FirstOrDefault(),
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

    public static IQueryable<Guid> OccupiedRoomIds(this AppDbContext db, DateOnly date) =>
        db.Contracts
            .Where(c => (c.Status == ContractStatus.Active || c.Status == ContractStatus.Liquidating || c.Status == ContractStatus.Ended)
                && c.RoomSince <= date && (c.ActualEndDate == null || c.ActualEndDate >= date))
            .Select(c => c.RoomId)
            .Union(db.Contracts.SelectMany(c => c.RoomMoves).Where(m => m.FromDate <= date && m.ToDate >= date).Select(m => m.RoomId));
}
