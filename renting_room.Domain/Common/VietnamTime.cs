namespace renting_room.Domain.Common;

/// <summary>Ngày nghiệp vụ theo giờ Việt Nam (C-04). Việt Nam không có giờ mùa hè nên dùng offset cố định +07:00.</summary>
public static class VietnamTime
{
    public static readonly TimeSpan Offset = TimeSpan.FromHours(7);

    public static DateOnly ToBusinessDate(this DateTimeOffset instant) =>
        DateOnly.FromDateTime(instant.ToOffset(Offset).DateTime);

    /// <summary>Tuổi tròn tại một ngày (đã qua sinh nhật năm đó mới tính thêm 1).</summary>
    public static int AgeOn(this DateOnly dateOfBirth, DateOnly date)
    {
        var age = date.Year - dateOfBirth.Year;
        return dateOfBirth.AddYears(age) > date ? age - 1 : age;
    }
}
