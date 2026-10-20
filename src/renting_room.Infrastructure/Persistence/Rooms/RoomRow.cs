using renting_room.Application.Common.Models;
using renting_room.Application.Rooms;
using renting_room.Domain.Common;
using renting_room.Domain.Contracts;
using renting_room.Domain.Properties;

namespace renting_room.Infrastructure.Persistence.Queries;

internal sealed class RoomRow
{
    public Room Room { get; init; } = null!;
    public string PropertyCode { get; init; } = null!;
    public CurrentContractDto? Current { get; init; }
    public RoomDisplayStatus Status { get; init; }

    public decimal OutstandingAmount { get; init; }

    public decimal OverdueAmount { get; init; }

    public decimal DepositHeld { get; init; }

    public RoomDto ToDto(IReadOnlyDictionary<Guid, IReadOnlyList<ContractFlag>> flags) => new(
        Room.Id, Room.PropertyId, PropertyCode, Room.Code, Room.Floor, Room.AreaM2, Room.MaxOccupants, Room.ListedRent,
        Room.DefaultDeposit, Room.Amenities, Room.Description, Status, Room.MaintenanceNote,
        Current is null
            ? null
            : Current with { Flags = flags.GetValueOrDefault(Current.Id, []).Where(f => f != ContractFlag.ExpiredAwaitingDecision).ToList() },
        Room.Version.ToString(), OutstandingAmount,
        OverdueAmount, DepositHeld);
}
