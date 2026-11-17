namespace renting_room.Domain.Notifications;

/// <summary>NT-BR-06 / NT-BR-19: bước nhắc nợ cần tạo cho một phiếu quá hạn.</summary>
public static class OverdueReminderPolicy
{
    public const int MaxSteps = 3;
    public const int MinDay = 1;
    public const int MaxDay = 60;

    /// <summary>≤ 3 phần tử, mỗi phần tử 1–60, không trùng, tăng dần.</summary>
    public static bool IsValid(IReadOnlyList<int> days) =>
        days.Count <= MaxSteps
        && days.All(d => d is >= MinDay and <= MaxDay)
        && days.Zip(days.Skip(1), (a, b) => a < b).All(ok => ok);

    /// <summary>
    /// Bước lớn nhất đã tới hạn (<c>dueDate + n ≤ today</c>) mà chưa tạo tin; null = không có gì phải tạo. Job lỡ ngày chạy bù ⇒
    /// chỉ ra bước lớn nhất, các bước nhỏ hơn bị bỏ (không dồn nhiều tin một lúc).
    /// </summary>
    public static int? DueStep(DateOnly dueDate, DateOnly today, IReadOnlyCollection<int> configuredDays, IReadOnlyCollection<int> sentSteps)
    {
        var due = configuredDays.Where(n => dueDate.AddDays(n) <= today).DefaultIfEmpty(0).Max();
        return due == 0 || sentSteps.Contains(due) ? null : due;
    }
}
