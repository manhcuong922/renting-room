namespace renting_room.Domain.Billing;

/// <param name="Month">Tháng thu (ngày 1 của tháng) — tháng của kỳ chuẩn của khu chứa kỳ này (C-05).</param>
public sealed record BillingPeriod(DateOnly Start, DateOnly End, DateOnly Month)
{
    /// <summary>Tháng thu dạng "yyyy-MM" (VD "2026-10").</summary>
    public string BillingMonth => $"{Month.Year:D4}-{Month.Month:D2}";

    public int Days => End.DayNumber - Start.DayNumber + 1;

    public bool Contains(DateOnly date) => date >= Start && date <= End;
}
