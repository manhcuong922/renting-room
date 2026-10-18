using FluentValidation;
using Microsoft.EntityFrameworkCore;
using renting_room.Application.Common.Interfaces;
using renting_room.Application.Contracts;
using renting_room.Domain.Billing;
using renting_room.Domain.Contracts;
using renting_room.Domain.Properties;

namespace renting_room.Application.Rooms;

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
    public static IQueryable<RoomRow> ToRows(this IQueryable<Room> rooms, IAppDbContext db, DateOnly today) =>
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
        this IReadOnlyList<RoomRow> rows, IAppDbContext db, DateOnly today, CancellationToken ct)
    {
        var flags = await ContractFlagReader.LoadAsync(db, rows.Where(r => r.Current is not null).Select(r => r.Current!.Id), today, ct);
        return rows.Select(r => r.ToDto(flags)).ToList();
    }

    /// <summary>
    /// PR-BR-02: phòng có HĐ đã bàn giao chiếm tại ngày <paramref name="date"/> — phòng hiện tại của HĐ (ngày trả phòng vẫn tính — CT-BR-33),
    /// hoặc phòng HĐ đã chuyển đi mà còn tính tới ngày chuyển (CT-BR-14).
    /// </summary>
    public static IQueryable<Guid> OccupiedRoomIds(this IAppDbContext db, DateOnly date) =>
        db.Contracts
            .Where(c => (c.Status == ContractStatus.Active || c.Status == ContractStatus.Liquidating || c.Status == ContractStatus.Ended)
                && c.RoomSince <= date && (c.ActualEndDate == null || c.ActualEndDate >= date))
            .Select(c => c.RoomId)
            .Union(db.Contracts.SelectMany(c => c.RoomMoves).Where(m => m.FromDate <= date && m.ToDate >= date).Select(m => m.RoomId));

    /// <summary>
    /// Phòng còn HĐ đang chiếm: hiệu lực / đang thanh lý / (tùy chọn) nháp, và HĐ đã kết thúc mà hôm nay vẫn là ngày trả phòng
    /// (người thuê còn ở hết ngày đó — khớp trạng thái "Đang thuê").
    /// </summary>
    public static Task<bool> HasOpenContractAsync(this IAppDbContext db, Guid roomId, bool includeDraft, DateOnly today, CancellationToken ct) =>
        db.Contracts.AnyAsync(c => c.RoomId == roomId
            && (c.Status == ContractStatus.Active || c.Status == ContractStatus.Liquidating
                || (c.Status == ContractStatus.Ended && c.ActualEndDate >= today)
                || (includeDraft && c.Status == ContractStatus.Draft)), ct);
}
