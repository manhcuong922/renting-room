namespace renting_room.Domain.Contracts;

/// <summary>Một khoảng HĐ ở 1 phòng (phòng cũ đã chuyển đi hoặc phòng hiện tại — <paramref name="To"/> null = chưa trả phòng).</summary>
public sealed record RoomStay(Guid RoomId, DateOnly From, DateOnly? To)
{
    public bool Overlaps(DateOnly from, DateOnly to) => From <= to && (To is null || To >= from);
}
