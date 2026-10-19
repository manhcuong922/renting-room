using renting_room.Application.Common.Models;
using renting_room.Application.Rooms;
using renting_room.Domain.Common;
using renting_room.Domain.Contracts;
using renting_room.Domain.Properties;

namespace renting_room.Infrastructure.Persistence.Queries;

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
