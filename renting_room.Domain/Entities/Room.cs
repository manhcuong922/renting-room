using renting_room.Domain.Common;
using renting_room.Domain.Enums;

namespace renting_room.Domain.Entities;

public class Room : Entity
{
    private Room() { } // EF Core

    public string Name { get; private set; } = null!;
    public decimal MonthlyRent { get; private set; }
    public RoomStatus Status { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    public static Room Create(string name, decimal monthlyRent, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Room name is required.", nameof(name));

        if (monthlyRent <= 0)
            throw new ArgumentException("Monthly rent must be greater than zero.", nameof(monthlyRent));

        return new Room
        {
            Id = Guid.NewGuid(),
            Name = name,
            MonthlyRent = monthlyRent,
            Status = RoomStatus.Available,
            CreatedAt = now
        };
    }

    public Result MarkOccupied()
    {
        if (Status is not RoomStatus.Available)
            return Result.Failure("Only available rooms can be marked occupied.");

        Status = RoomStatus.Occupied;
        return Result.Success();
    }

    public Result MarkAvailable()
    {
        if (Status is RoomStatus.Available)
            return Result.Failure("Room is already available.");

        Status = RoomStatus.Available;
        return Result.Success();
    }
}
